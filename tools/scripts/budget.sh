#!/usr/bin/env bash
# Time budgets (CLAUDE.md rule 14): budget.sh [log dir]. Compares the test and sweep logs a check wrote
# with tools/scripts/time-budget.txt. BUDGET line at 1.5x a budget or more (unlisted tests: over 10 s);
# RUNAWAY line at 5x or over 10 min. Exit 1 only on a runaway: over budget never blocks a merge.
dir="${1:-out/check}"; map="$(dirname "$0")/time-budget.txt"
{
  [ -f "$dir/test.log" ] && awk -v map="$map" '
    BEGIN { while ((getline l < map) > 0) { split(l, f, " "); if (f[1] == "test") b[f[2]] = f[3] } }
    ($1 == "Passed" || $1 == "Failed") && ($NF == "s]" || $NF == "ms]") {
      n = $2; sub(/\(.*/, "", n); k = split(n, p, "."); n = p[k - 1] "." p[k]; t = 0
      for (i = 3; i < NF; i++) { v = $i; gsub(/\[/, "", v); u = $(i + 1)
        if (u == "m") t += v * 60; else if (u == "s]" || u == "s") t += v; else if (u == "ms]") t += v / 1000 }
      if (t > worst[n]) worst[n] = t
    }
    END {
      for (n in worst) { t = worst[n]; lim = (n in b) ? b[n] : 10
        if (t >= 5 * lim || t > 600) printf "RUNAWAY test %s: %d s, budget %d s\n", n, t, lim
        else if ((n in b) ? t >= 1.5 * lim : t > lim) printf "BUDGET test %s: %d s, budget %d s%s\n", n, t, lim, (n in b) ? "" : " (unlisted)" }
    }' "$dir/test.log"
  for size in 256 512; do
    [ -f "$dir/sweep$size.log" ] && awk -v map="$map" -v size="$size" '
      BEGIN { while ((getline l < map) > 0) { split(l, f, " "); if (f[1] == "sweep" && f[2] == size) { med[f[3]] = f[4]; mx[f[3]] = f[5] } } }
      function t(ms) { return ms >= 10000 ? sprintf("%d s", ms / 1000) : sprintf("%.0f ms", ms) }
      $1 == "check" && $2 == "groups:" { ing = 1; next }
      $2 == "median" && (g = (ing ? $1 : ($1 == "checks" ? "checks" : ""))) in med {
        m = $3; x = $6
        w = (m >= 5 * med[g] || x >= 5 * mx[g] || x > 600000) ? "RUNAWAY" : (m >= 1.5 * med[g] || x >= 1.5 * mx[g]) ? "BUDGET" : ""
        if (w != "") printf "%s sweep %s %s: median %s, max %s; budget %s, %s\n", w, size, g, t(m), t(x), t(med[g]), t(mx[g])
      }' "$dir/sweep$size.log"
  done
} | sort > "$dir/budget.out"
cat "$dir/budget.out"
! grep -q '^RUNAWAY' "$dir/budget.out"
