#!/usr/bin/env bash
# Lane check: one call, summary lines only. Usage: check.sh [test-filter] [--no-unity] [--full]
# Exit 1 if anything fails. Full logs stay in out/check/.
. "$(dirname "$0")/env.sh"; cd "$ROOT"; mkdir -p out/check
filter="${1:-FullyQualifiedName~Feature|FullyQualifiedName~Object|FullyQualifiedName~Discover}"
fail=0; args=" $* "
say() { echo "$*"; }
if ! dotnet build -c Release -v q > out/check/build.log 2>&1; then say "build: FAILED"; grep -E "error" out/check/build.log | head -10; exit 1; fi
say "build: ok"
if [[ "$args" == *" --full "* ]]; then targs=(); else targs=(--filter "$filter"); fi
dotnet test -c Release --no-build "${targs[@]}" > out/check/test.log 2>&1 || fail=1
say "tests: $(grep -E "Passed!|Failed!" out/check/test.log | sed -E 's/ - Stabby.*//' | tr -s ' ')"
grep -E "^\s+Failed " out/check/test.log | head -5
for spec in "100 256" "20 512"; do set -- $spec
  dotnet run --project tools/Stabby.Cli -c Release --no-build -- sweep --count $1 --size $2 > out/check/sweep$2.log 2>&1 || fail=1
  say "sweep $2: $(grep -i -E "violation|median|max" out/check/sweep$2.log | tail -2 | tr '\n' ' ')"
done
if [[ "$args" != *" --no-unity "* ]]; then
  "$UNITY_EXE" -batchmode -quit -projectPath unity -logFile out/check/unity.log > /dev/null 2>&1
  n=$(grep -c "error CS" out/check/unity.log); say "unity: $n CS errors"; [ "$n" = 0 ] || { fail=1; grep "error CS" out/check/unity.log | sort -u | head -5; }
fi
base=$(git merge-base HEAD origin/main)
bad=$(git diff --name-only "$base" | xargs -r git ls-files --eol 2>/dev/null | grep -v -E "w/(lf|-text|none)" | grep -v "binary")
[ -z "$bad" ] && say "eol: ok" || { say "eol: NOT LF"; echo "$bad"; fail=1; }
nc=$(git diff "$base" -- '*.cs' '*.shader' '*.hlsl' '*.compute' | grep -c -E "^\+\s*(//|/\*)")
say "new comment lines: $nc (reason lines only, rule 7)"
exit $fail
