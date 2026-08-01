#!/usr/bin/env bash
# DAW — Copilot CLI · postToolUse: revalidate the state on disk.
#
# The net under the net: it catches writes no pre-write matcher sees, which in
# practice means a Bash/jq/sed edit of the state file. It refuses (exit 2)
# rather than merely reporting — an inconsistent state that is only mentioned is
# an inconsistent state that stays.
set -uo pipefail

REPO="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"

cat > /dev/null            # drain the event; post mode reads the disk, not stdin

# Repo first, plugin second; the user-level copy stands down where the repo
# wires its own hook. Same reasoning as pre-tool-use.sh.
if [ -n "${DAW_PLUGIN_ROOT:-}" ] && [ -f "$REPO/.github/hooks/daw/post-write.sh" ]; then
  echo '{}'
  exit 0
fi
DAW="$REPO/.daw"
if [ ! -f "$DAW/scripts/hook-gate.py" ]; then
  if [ -n "${DAW_PLUGIN_ROOT:-}" ] && [ -f "$DAW_PLUGIN_ROOT/daw/scripts/hook-gate.py" ]; then
    DAW="$DAW_PLUGIN_ROOT/daw"
  else                      # DAW is not reachable from here
    echo '{}'
    exit 0
  fi
fi
GATE="$DAW/scripts/hook-gate.py"
STATE="$REPO/.daw-state.json"
GRAPH="$DAW/rules/transition-graph.json"

exec python3 "$GATE" --dialect copilot --mode post --state "$STATE" --graph "$GRAPH" --repo "$REPO"
