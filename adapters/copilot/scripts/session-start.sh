#!/usr/bin/env bash
# DAW — Copilot CLI · sessionStart: boot the pipeline for this session.
#
# The message goes back under `additionalContext`, which is the key Copilot
# actually reads. It used to be `context`, so the one nudge that starts the
# pipeline was computed, formatted and silently dropped.
set -uo pipefail

REPO="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
# Repo first, plugin second; the user-level copy stands down where the repo
# wires its own hook. Same reasoning as pre-tool-use.sh.
if [ -n "${DAW_PLUGIN_ROOT:-}" ] && [ -f "$REPO/.github/hooks/daw/session-start.sh" ]; then
  echo '{}'
  exit 0
fi
DAW="$REPO/.daw"
if [ ! -d "$DAW" ] && [ -n "${DAW_PLUGIN_ROOT:-}" ] && [ -d "$DAW_PLUGIN_ROOT/daw" ]; then
  DAW="$DAW_PLUGIN_ROOT/daw"
fi
BOOT="$DAW/scripts/session-boot.py"
[ -f "$BOOT" ] || { echo '{}'; exit 0; }

IN="$(cat 2>/dev/null || true)"
SID="$(printf '%s' "$IN" | python3 -c "
import json,sys
try: print(json.load(sys.stdin).get('sessionId') or '')
except Exception: print('')" 2>/dev/null || true)"
[ -n "$SID" ] || SID="copilot-$$"

exec python3 "$BOOT" --repo "$REPO" --session-id "$SID" --format json --method "$DAW"
