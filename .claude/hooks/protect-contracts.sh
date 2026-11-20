#!/usr/bin/env bash
root="${CLAUDE_PROJECT_DIR:-$(cd "$(dirname "$0")/../.." && pwd)}"
ROOT="$root" python -c '
import json, os, sys
try:
    path = json.load(sys.stdin).get("tool_input", {}).get("file_path", "")
except Exception:
    sys.exit(0)
def norm(p):
    p = p.replace("\\", "/")
    # Git Bash reports /d/x while tools send D:/x.
    if len(p) > 2 and p[0] == "/" and p[2] == "/":
        p = p[1] + ":" + p[2:]
    return os.path.normcase(os.path.normpath(p))
root = os.environ["ROOT"]
target = path if os.path.isabs(path) or path.startswith("/") else os.path.join(root, path)
if path and norm(target) == norm(os.path.join(root, "CLAUDE.md")):
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse", "permissionDecision": "ask",
        "permissionDecisionReason": "CLAUDE.md is the architecture contract; foundational changes need Zach'"'"'s review."}}))
'
