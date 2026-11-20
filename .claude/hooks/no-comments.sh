#!/usr/bin/env bash
path=$(python -c 'import json,sys
try: print(json.load(sys.stdin).get("tool_input",{}).get("file_path",""))
except Exception: pass')
case "$path" in *.cs) ;; *) exit 0 ;; esac
[ -f "$path" ] || exit 0
hits=$(grep -nE '^\s*(//|/\*)|[;{}),]\s*(//|/\*)' "$path" | head -5)
[ -z "$hits" ] && exit 0
echo "Stabby has no code comments (CLAUDE.md rule 7); remove these and put the reason in the commit message:" >&2
echo "$hits" >&2
exit 2
