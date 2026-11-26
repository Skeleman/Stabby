#!/usr/bin/env python3
"""Where check time goes: lane-time.py [--since YYYY-MM-DD] [--top N].

Reads .git/stabby-checks.csv (one row per check.sh run: date, branch, mode, gen, wait s, run s, fail)
and prints totals per branch and per group (check mode, with +gen when generation sweeps ran), then the
slowest tests and sweep check groups from the latest out/check logs of the main tree and every worktree."""
import argparse
import collections
import glob
import os
import re
import subprocess

TEST = re.compile(r"^\s*(Passed|Failed)\s+(\S+)\s+\[(.+)\]\s*$")
SWEEP = re.compile(r"^\s*(\S+)\s+median\s+([\d.]+)\s+ms.*?max\s+([\d.]+)\s+ms")


def git_common():
    out = subprocess.run(["git", "rev-parse", "--git-common-dir"], capture_output=True, text=True, check=True).stdout.strip()
    return os.path.abspath(out)


def seconds(text):
    t = 0.0
    for v, u in re.findall(r"([\d.]+)\s*(ms|m|s)", text):
        t += float(v) * {"ms": 0.001, "s": 1, "m": 60}[u]
    return t


def hms(s):
    s = int(round(s))
    return f"{s // 3600}h{s % 3600 // 60:02d}m" if s >= 3600 else f"{s // 60}m{s % 60:02d}s"


def table(title, rows):
    print(f"\n{title}")
    print(f"  {'name':<34} {'runs':>5} {'fail':>5} {'run':>9} {'wait':>9} {'mean run':>9}")
    for name, r in sorted(rows.items(), key=lambda kv: -kv[1][2]):
        print(f"  {name:<34} {r[0]:>5} {r[1]:>5} {hms(r[2]):>9} {hms(r[3]):>9} {hms(r[2] / r[0]):>9}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--since", default="")
    ap.add_argument("--top", type=int, default=15)
    a = ap.parse_args()
    common = git_common()
    by_branch = collections.defaultdict(lambda: [0, 0, 0.0, 0.0])
    by_group = collections.defaultdict(lambda: [0, 0, 0.0, 0.0])
    total = [0, 0, 0.0, 0.0]
    with open(os.path.join(common, "stabby-checks.csv"), encoding="utf-8") as f:
        for line in f:
            p = line.strip().split(",")
            if len(p) < 7 or p[0] < a.since:
                continue
            group = p[2] + ("+gen" if p[3] == "1" else "")
            for r in (by_branch[p[1]], by_group[group], total):
                r[0] += 1
                r[1] += p[6] != "0"
                r[2] += float(p[5])
                r[3] += float(p[4])
    if not total[0]:
        print("no check runs")
        return
    print(f"{total[0]} check runs{' since ' + a.since if a.since else ''}: {hms(total[2])} running, {hms(total[3])} waiting for the check lock, {total[1]} failed")
    table("per group", by_group)
    table("per branch", by_branch)

    root = os.path.dirname(common)
    dirs = [os.path.join(root, "out", "check")] + glob.glob(os.path.join(root, ".claude", "worktrees", "*", "out", "check"))
    tests, sweeps = {}, {}
    for d in dirs:
        log = os.path.join(d, "test.log")
        if os.path.isfile(log):
            with open(log, encoding="utf-8", errors="replace") as f:
                for line in f:
                    m = TEST.match(line)
                    if m:
                        name = ".".join(re.sub(r"\(.*", "", m.group(2)).split(".")[-2:])
                        t = seconds(m.group(3))
                        if t > tests.get(name, (0, ""))[0]:
                            tests[name] = (t, os.path.basename(os.path.dirname(os.path.dirname(d))))
        for size in ("256", "512"):
            log = os.path.join(d, f"sweep{size}.log")
            if os.path.isfile(log):
                with open(log, encoding="utf-8", errors="replace") as f:
                    for line in f:
                        m = SWEEP.match(line)
                        if m:
                            key = f"{size} {m.group(1)}"
                            sweeps[key] = max(sweeps.get(key, (0, 0, "")), (float(m.group(2)), float(m.group(3)), os.path.basename(os.path.dirname(os.path.dirname(d)))))
    print(f"\nslowest tests (worst of {len(dirs)} trees' latest check logs)")
    for name, (t, tree) in sorted(tests.items(), key=lambda kv: -kv[1][0])[: a.top]:
        print(f"  {t:7.1f} s  {name}  ({tree})")
    if sweeps:
        print("\nslowest sweep check groups (per-seed median, max)")
        for key, (med, mx, tree) in sorted(sweeps.items(), key=lambda kv: -kv[1][0])[: a.top]:
            print(f"  {med / 1000:7.2f} s {mx / 1000:7.2f} s  sweep {key}  ({tree})")


if __name__ == "__main__":
    main()
