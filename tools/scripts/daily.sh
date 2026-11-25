#!/usr/bin/env bash
# Daily full check of origin/main in its own worktree (merges sweep fewer seeds). Records the last
# passing commit; on a failure lists the merges since it, so the culprit is among them. Appends one
# timing row to stabby-timings.csv and prints a TREND warning for any value 25% over its 7-run median.
. "$(dirname "$0")/env.sh"; cd "$ROOT"
common="$(cd "$(git rev-parse --git-common-dir)" && pwd)"; tree="$common/stabby-daily"; last="$common/stabby-daily-pass"
csv="$common/stabby-timings.csv"
git fetch -q origin
if [ -d "$tree" ]; then git -C "$tree" checkout -q --detach origin/main; else git worktree add -q --detach "$tree" origin/main; fi
sha=$(git -C "$tree" rev-parse --short HEAD)
mkdir -p "$tree/out"
(cd "$tree" && bash tools/scripts/check.sh "" --full --no-unity) | tee "$tree/out/daily.txt"; rc=${PIPESTATUS[0]}
log="$tree/out/check"
total=$(sed -n -E 's/^total: ([0-9]+) s$/\1/p' "$tree/out/daily.txt")
tests=$(head -1 "$log/tests.sum" 2> /dev/null | sed -n -E 's/.*, ([0-9]+) s$/\1/p')
slow=$(sed -n -E 's/^slowest: //p' "$log/tests.sum" 2> /dev/null | awk -F', ' '{ for (i = 1; i <= 3; i++) { split($i, a, " "); printf ",%s,%s", a[1], a[2] } }')
[ -n "$slow" ] || slow=",,,,,,"
sweep() { awk '$1 == "checks" && $2 == "median" { printf "%.1f", $3 / 1000 }' "$log/sweep$1.log" 2> /dev/null; }
row="$(date +%F),$sha,$total,$tests$slow,$(sweep 256),$(sweep 512)"
[ -f "$csv" ] || echo "date,sha,total,tests,slow1,slow1_s,slow2,slow2_s,slow3,slow3_s,sweep256,sweep512" > "$csv"
tail -n +2 "$csv" | tail -7 | awk -F, -v row="$row" '
  function med(name, vals, shas, n,   i, j, t, s) {
    for (i = 2; i <= n; i++) for (j = i; j > 1 && vals[j - 1] > vals[j]; j--) { t = vals[j]; vals[j] = vals[j - 1]; vals[j - 1] = t; s = shas[j]; shas[j] = shas[j - 1]; shas[j - 1] = s }
    m = int((n + 1) / 2); if (n > 0 && cur[name] != "" && vals[m] > 0 && cur[name] >= 1.25 * vals[m]) print name, cur[name], vals[m], shas[m]
  }
  { r[NR] = $0 }
  END {
    split(row, c, ","); cur["total"] = c[3]; cur["tests"] = c[4]; cur["sweep256"] = c[11]; cur["sweep512"] = c[12]
    col["total"] = 3; col["tests"] = 4; col["sweep256"] = 11; col["sweep512"] = 12
    for (k in col) { n = 0; delete v; delete h
      for (i = 1; i <= NR; i++) { split(r[i], f, ","); if (f[col[k]] != "") { n++; v[n] = f[col[k]] + 0; h[n] = f[2] } }
      med(k, v, h, n) }
    for (s = 5; s <= 9; s += 2) { name = c[s]; if (name == "") continue; cur[name] = c[s + 1]; n = 0; delete v; delete h
      for (i = 1; i <= NR; i++) { split(r[i], f, ","); for (j = 5; j <= 9; j += 2) if (f[j] == name) { n++; v[n] = f[j + 1] + 0; h[n] = f[2] } }
      med(name, v, h, n) }
  }' | while read -r name now was from; do
  echo "TREND $name: $now s, median of the last 7 runs $was s (run $from); merges since $from:"
  git log --oneline --merges --first-parent "$from..$sha" | head -15
done
echo "$row" >> "$csv"
if [ $rc = 0 ]; then echo "$sha" > "$last"; echo "DAILY PASS $sha"
else
  echo "DAILY FAILED $sha; merges since the last pass ($(cat "$last" 2> /dev/null || echo none)):"
  [ -f "$last" ] && git log --oneline --merges "$(cat "$last")..$sha" | head -30
  exit 1
fi
