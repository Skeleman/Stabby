#!/usr/bin/env bash
# Lane check: one call, summary lines only. Usage: check.sh [test-filter] [--no-unity] [--full|--merge]
# Build, then tests, sweeps and the Unity compile at once; phases the diff cannot affect are skipped
# (--full forces all; --merge forces all but sweeps only 25+5 seeds, and only when the diff can change
# generation; daily.sh sweeps all). Tests run in full when the diff touches src/, tests/ or content/, else follow tools/scripts/test-impact.txt; Category=Sweep tests skip
# when sweeps run, except TwentySeedsPassEveryCheck under --merge. Time budgets (budget.sh) warn; only a runaway fails. One check per machine at a time.
# Exit 1 if anything fails. Logs in out/check/.
# Without --full, sweeps run only the check groups tools/scripts/sweep-impact.txt maps the diff to.
. "$(dirname "$0")/env.sh"; cd "$ROOT"; mkdir -p out/check
args=" $* "; full=0; [[ "$args" == *" --full "* ]] && full=1
merge=0; [[ "$args" == *" --merge "* ]] && merge=1
nounity=0; [[ "$args" == *" --no-unity "* ]] && nounity=1
say() { echo "$*"; }
common="$(cd "$(git rev-parse --git-common-dir)" && pwd)"; lock="$common/stabby-check.lock"; passes="$common/stabby-check-pass"
t_wait=$SECONDS; told=0
until mkdir "$lock" 2> /dev/null; do
  opid=""; ostart=""; otree=""; read -r opid ostart otree < "$lock/owner" 2> /dev/null
  [ -z "$ostart" ] && [ -n "$(find "$lock" -maxdepth 0 -mmin +2 2> /dev/null)" ] && { rm -rf "$lock"; continue; }
  if [ -n "$ostart" ] && { ! kill -0 "$opid" 2> /dev/null || [ $(( $(date +%s) - ostart )) -gt 7200 ]; }; then rm -rf "$lock"; continue; fi
  [ $told = 0 ] && { say "lock: waiting for the check in ${otree:-another tree}"; told=1; }
  [ $((SECONDS - t_wait)) -ge 5400 ] && { say "lock: gave up after 90 min"; exit 1; }
  sleep 5
done
echo "$$ $(date +%s) $ROOT" > "$lock/owner"; trap 'rm -rf "$lock" out/check/busy' EXIT; trap 'exit 130' INT TERM
[ $told = 1 ] && say "lock: waited $((SECONDS - t_wait)) s"
t0=$SECONDS
base=$(git merge-base HEAD origin/main)
changed=$({ git diff --name-only "$base"; git ls-files --others --exclude-standard; } | sort -u)
has() { grep -q -E "$1" <<< "$changed"; }
gen=0; game=0; unity=0
if [ $full = 1 ] || has '^(Directory\.Build\.[a-z]+|[^/]+\.sln[x]?|global\.json|NuGet\.config|\.editorconfig|tools/scripts/(check|env)\.sh$)'; then gen=1; game=1; unity=1
elif [ $merge = 1 ]; then game=1; unity=1; has '^(src/|content/|tools/Stabby\.Cli/)' && gen=1
else
  has '^(src/|content/|tools/Stabby\.Cli/)' && gen=1
  has '^(unity/|tools/Stabby\.Lint/|lint-allow\.txt)' && game=1
  has '^(unity/|src/)' && unity=1
fi
[ $nounity = 1 ] && unity=0
sweep_groups() {
  local f pat sel hit groups=core
  while read -r f; do
    [[ "$f" =~ ^(src/|content/|tools/Stabby\.Cli/) ]] || continue
    hit=0
    while read -r pat sel; do
      [ -n "$pat" ] && [[ "$f" == $pat ]] || continue
      hit=1; [ "$sel" = all ] && { echo all; return; }
      groups="$groups,$sel"
    done < tools/scripts/sweep-impact.txt
    [ $hit = 0 ] && { echo all; return; }
  done <<< "$changed"
  tr ',' '\n' <<< "$groups" | sort -u | paste -sd, -
}
sg=all
[ $full = 0 ] && ! has '^(Directory\.Build\.[a-z]+|[^/]+\.sln[x]?|global\.json|NuGet\.config|\.editorconfig|tools/scripts/(check|env)\.sh$)' && sg=$(sweep_groups)
impact() {
  local f g line hit fb="" out="" globs map=tools/scripts/test-impact.txt
  while IFS= read -r line; do [ "${line%% -> *}" = fallback ] && fb="${line#* -> }"; done < $map
  while IFS= read -r f; do
    [ -z "$f" ] && continue; hit=0; f=${f%.meta}
    [[ $f =~ ^tests/.*/([A-Za-z]+Tests)\.cs$ ]] && { out="$out|${BASH_REMATCH[1]}"; hit=1; }
    while IFS= read -r line; do
      read -ra globs <<< "${line%% -> *}"
      for g in "${globs[@]}"; do [[ $f == $g ]] && { out="$out|${line#* -> }"; hit=1; break; }; done
    done < $map
    [ $hit = 0 ] && [[ $f =~ ^(src/|content/|tests/|tools/Stabby\.(Cli|Lint)/|Directory\.Build|[^/]+\.slnx?$|global\.json|NuGet\.config|\.editorconfig) ]] && out="$out|$fb"
  done <<< "$changed"
  echo "$out"
}
explicit=0
if [ $full = 1 ] || [ $merge = 1 ]; then tf=""
elif [ -n "$1" ] && [[ "$1" != --* ]]; then tf="$1"; explicit=1
elif has '^(src/|tests/|content/|Directory\.Build\.[a-z]+|[^/]+\.sln[x]?|global\.json|NuGet\.config|\.editorconfig|tools/scripts/(check|env)\.sh$)'; then tf=""
else
  terms="Lint$(impact)"
  { [ $game = 1 ] || [ $unity = 1 ]; } && terms="$terms|ChunkZip|RuntimeParity|PoolSize"
  tf=$(tr '|' '\n' <<< "$terms" | grep -v '^$' | sort -u | sed 's/^/FullyQualifiedName~/' | paste -sd'|' -)
fi
parity=0
if [ $unity = 1 ] && { [ -z "$tf" ] || [[ "$tf" == *RuntimeParity* ]]; }; then parity=1; tf="${tf:+($tf)&}FullyQualifiedName!~RuntimeParity"; fi
[ $gen = 1 ] && [ $explicit = 0 ] && { sw="Category!=Sweep"; [ $merge = 1 ] && sw="($sw|FullyQualifiedName~TwentySeedsPassEveryCheck)"; tf="${tf:+($tf)&}$sw"; }
say "phases: tests${tf:+ ($tf)}$([ $gen = 1 ] && echo ", sweeps ($sg)")$([ $unity = 1 ] && echo ", unity")"
if ! dotnet build -c Release -v q > out/check/build.log 2>&1; then say "build: FAILED"; grep -E "error" out/check/build.log | head -10; exit 1; fi
say "build: ok, $((SECONDS - t0)) s"
if ! dotnet run --project tools/Stabby.Lint -c Release --no-build -- . > out/check/lint.log 2>&1; then say "lint: FAILED"; grep -v "^lint" out/check/lint.log | head -10; exit 1; fi
say "lint: ok, $((SECONDS - t0)) s"
rm -f out/check/*.sum out/check/*.rc out/check/sweep256.log out/check/sweep512.log
phase_tests() {
  local s=$SECONDS rc=0 targs=(); [ -n "$tf" ] && targs=(--filter "$tf")
  dotnet test -c Release --no-build "${targs[@]}" --blame-hang-timeout 10m --blame-hang-dump-type none --logger "console;verbosity=normal" > out/check/test.log 2>&1 || rc=1
  { echo "tests: $(grep -h -E "No test matches|^Test Run (Successful|Failed|Aborted)" out/check/test.log | head -1) $(sed -n -E 's/^ *(Total tests|Passed|Failed): ([0-9]+)/\1 \2/p' out/check/test.log | paste -sd, - | sed 's/,/, /g'), $((SECONDS - s)) s"
    grep -E "^\s+Failed " out/check/test.log | head -5
    echo "slowest: $(awk '$1 == "Passed" && $NF == "s]" { t = 0; n = $2; sub(/\(.*/, "", n); sub(/.*\./, "", n); for (i = 3; i < NF; i++) { v = $i; gsub(/\[/, "", v); if ($(i+1) == "m") t += v * 60; else if ($(i+1) == "s]") t += v } print t, n }' out/check/test.log | sort -rn | head -3 | awk '{ printf "%s%s %d s", (NR > 1 ? ", " : ""), $2, $1 }')"; } > out/check/tests.sum
  grep -q "No test matches" out/check/test.log && rc=0
  echo $rc > out/check/tests.rc
}
phase_sweep() {
  local s=$SECONDS rc=0 line
  dotnet run --project tools/Stabby.Cli -c Release --no-build -- sweep --count $1 --size $2 --groups $sg --low-while out/check/busy > out/check/sweep$2.log 2>&1 || rc=1
  line=$(grep -E "seeds at" out/check/sweep$2.log | tail -1); [[ "$line" == *" 0 violations"* ]] || rc=1
  echo "sweep $2: $line, $((SECONDS - s)) s" > out/check/sweep$2.sum; echo $rc > out/check/sweep$2.rc
}
phase_unity() {
  local s=$SECONDS done_at=0 stuck="" rc=0 up n
  rm -f out/check/unity.log
  "$UNITY_EXE" -batchmode -nographics -timestamps -quit -projectPath unity -logFile out/check/unity.log > /dev/null 2>&1 & up=$!
  while kill -0 $up 2> /dev/null; do
    [ $done_at = 0 ] && grep -q "CodeReloadManager destroyed" out/check/unity.log 2> /dev/null && done_at=$SECONDS
    if { [ $done_at != 0 ] && [ $((SECONDS - done_at)) -ge 30 ]; } || [ $((SECONDS - s)) -ge 1800 ]; then
      stuck=$([ $done_at = 0 ] && echo ", timed out" || echo ", killed $((SECONDS - done_at)) s into a shutdown hang"); taskkill //F //PID "$(cat /proc/$up/winpid)" > /dev/null 2>&1; break
    fi
    sleep 2
  done
  wait $up 2> /dev/null
  [ "$stuck" = ", timed out" ] && rc=1
  n=$(grep -c -E "error CS|Burst error" out/check/unity.log); [ "$n" = 0 ] || rc=1
  { echo "unity: $n CS/Burst errors, $((SECONDS - s)) s$stuck"; grep -E "error CS|Burst error" out/check/unity.log | sort -u | head -5; } > out/check/unity.sum
  if [ $rc = 0 ] && [ $parity = 1 ]; then
    dotnet test -c Release --no-build --filter "FullyQualifiedName~RuntimeParity" > out/check/parity.log 2>&1 || rc=1
    echo "parity: $(grep -E "Passed!|Failed!" out/check/parity.log | sed -E 's/ - Stabby.*//' | tr -s ' ')" >> out/check/unity.sum
  fi
  echo $rc > out/check/unity.rc
}
touch out/check/busy
{ phase_tests & [ $unity = 1 ] && phase_unity & wait; rm -f out/check/busy; } &
n256=100; n512=20; [ $merge = 1 ] && { n256=25; n512=5; }
[ $gen = 1 ] && { phase_sweep $n256 256 & phase_sweep $n512 512 & }
wait
fail=0
for p in tests sweep256 sweep512 unity; do
  [ -f out/check/$p.sum ] && cat out/check/$p.sum
  [ -f out/check/$p.rc ] && [ "$(cat out/check/$p.rc)" != 0 ] && fail=1
done
[ -f out/check/tests.rc ] || fail=1
if ! bash tools/scripts/budget.sh out/check; then
  # A whole-machine stall freezes every phase at once; only a runaway that repeats alone fails
  cp out/check/budget.out out/check/budget-first.out
  say "runaway: rerunning the runaway sweeps and tests alone"
  for z in $(sed -n -E 's/^RUNAWAY sweep ([0-9]+) .*/\1/p' out/check/budget-first.out | sort -u); do
    cp out/check/sweep$z.log out/check/sweep$z-first.log
    phase_sweep $([ $z = 256 ] && echo $n256 || echo $n512) $z; cat out/check/sweep$z.sum; [ "$(cat out/check/sweep$z.rc)" != 0 ] && fail=1
  done
  rt=$(sed -n -E 's/^RUNAWAY test ([^:]+):.*/FullyQualifiedName~\1/p' out/check/budget-first.out | paste -sd'|' -)
  mv out/check/test.log out/check/test-first.log 2> /dev/null
  if [ -n "$rt" ]; then
    dotnet test -c Release --no-build --filter "$rt" --logger "console;verbosity=normal" > out/check/test.log 2>&1 || fail=1
  fi
  bash tools/scripts/budget.sh out/check || fail=1
fi
bad=$(git diff --name-only "$base" | xargs -r git ls-files --eol 2> /dev/null | grep -v -E "w/(lf|-text|none)" | grep -v "binary")
[ -z "$bad" ] && say "eol: ok" || { say "eol: NOT LF"; echo "$bad"; fail=1; }
nc=$(git diff "$base" -- '*.cs' '*.shader' '*.hlsl' '*.compute' | grep -c -E "^\+\s*(//|/\*)")
say "new comment lines: $nc (reason lines only, rule 7)"
say "total: $((SECONDS - t0)) s"
[ $merge = 1 ] && [ $((SECONDS - t0)) -gt 720 ] && say "BUDGET merge check: $((SECONDS - t0)) s, budget 720 s"
if [ $fail = 0 ] && { [ $full = 1 ] || [ $merge = 1 ]; } && [ $nounity = 0 ]; then
  dirty=$(git status --porcelain --untracked-files=no | cut -c4- | grep -v -E '^unity/(Assets/Scenes/|ProjectSettings/|Assets/[^/]+\.asset$)')
  if [ -z "$dirty" ]; then
    mkdir -p "$passes"; find "$passes" -type f -mtime +7 -delete 2> /dev/null
    echo "$(date '+%F %T') $(git branch --show-current) $(git rev-parse --short HEAD)" > "$passes/$(git rev-parse 'HEAD^{tree}')"
    say "cache: full pass recorded for tree $(git rev-parse --short 'HEAD^{tree}')"
  fi
fi
mode=scoped; [ $merge = 1 ] && mode=merge; [ $full = 1 ] && mode=full
echo "$(date '+%F %T'),$(git branch --show-current),$mode,$gen,$((t0 - t_wait)),$((SECONDS - t0)),$fail" >> "$common/stabby-checks.csv"
exit $fail
