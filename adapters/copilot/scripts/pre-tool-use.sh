#!/usr/bin/env bash
# DAW — Copilot CLI · preToolUse: refuse a write that would break the FSM.
#
# The whole job is: hand the event to the shared gate without touching it. The
# previous version read stdin into a variable first, so by the time the
# validator ran there was nothing left to read; it saw an empty envelope, exited
# 0, and every illegal transition went through. Copilot looked installed and
# enforced nothing.
#
# `--dialect copilot` covers the second half: Copilot's envelope is camelCase
# (`toolName`/`toolArgs`), and its refusal is a JSON verdict on stdout as well
# as exit 2.
set -uo pipefail

REPO="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
# Repo first, plugin second — the same order every adapter resolves in. As the
# user-level copy (DAW_PLUGIN_ROOT set, wired in ~/.copilot/config.json) this
# script stands down when the repo wires its own hook: otherwise every write
# would be judged twice, once per level.
if [ -n "${DAW_PLUGIN_ROOT:-}" ] && [ -f "$REPO/.github/hooks/daw/pre-tool-use.sh" ]; then
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

exec python3 "$GATE" --dialect copilot --mode pre --state "$STATE" --graph "$GRAPH" --repo "$REPO"
