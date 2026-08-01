# Changelog

All notable changes to DAW are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## What semantic versioning covers here

DAW is a method plus the wiring that plugs it into a coding agent, and those two
move at different speeds. So the promise is specific:

- **Covered.** The method itself: the phases, the transition graph's format, the
  gate names, the artifact layout under `docs/daw/`, the state schema, and the
  adapter recipe schema. A breaking change to any of these is a major version.
- **Not covered.** The wiring for each individual tool. When Claude Code, Codex,
  Copilot, Cursor, Gemini or OpenCode changes its hook contract, that adapter
  follows it — in a patch release if it can, immediately either way. Tracking
  someone else's product is not a promise DAW can make on a version number.

---

## [1.0.1] — Unreleased

### Fixed

- **Claude Code reported a plugin error on every session.** It scans
  `hooks/hooks.json` at the plugin root whatever the manifest declares, and that
  path held Gemini's extension hooks — whose event names Claude cannot parse.
  Gemini's file moved to `adapters/gemini/`, and the root path is now asserted
  empty rather than asserted correct. Found by installing the plugin and reading
  the screen, which is what `scripts/acceptance.md` exists for.
- **The suite ran a smaller version of itself on macOS.** `find -printf`,
  `sed -i` and `shopt -s globstar` are GNU-only; on bash 3.2 they silently
  removed 21 checks and made one pass by comparing nothing to nothing.

### Changed

- **One product, one version.** All five manifests and the CHANGELOG must state
  the same number, a change to anything shipped has to move it, and each rule
  file still carries its own. `CONTRIBUTING.md` has the rule and
  `check_versions.py` enforces it, on pushes as well as pull requests.

---

## [1.0.0] — Unreleased

**First public release.**

The method has been in daily internal use since **February 2026**. This is its
first public extraction: the same pipeline, separated from the projects it grew
in, with the tool-specific wiring generalised from one agent to six.

### The method

- Six phases — `CLASSIFY → DEFINE → PLAN → CODE → VERIFY → RELEASE` — with a
  gate between each pair and state that survives closing the session.
- Five tiers, so the ceremony matches the size of the request: `QUERY`,
  `QUICK-FIX`, `FIX`, `FEATURE`, `DISCOVERY`.
- Gates enforced by a hook running outside the model, not asked for in a prompt.
- Gates graded by what backs them, and the grade written down: the PRD's rests on
  a content-hashed receipt, the commit gate asks git, and the rest are the model's
  record. `tests` and `sast` stay self-declared deliberately — see RATIONALE 16.
- An artifact per phase, under `docs/daw/`, committed as that phase closes.
- Seventeen skills and five subagents, including auditors that did not write the
  code they review.
- Security as two phases of the pipeline rather than a review afterwards: threat
  modeling in PLAN, SAST in CODE. Deliberately no dynamic scan — see the README
  for why a gate that cannot be satisfied honestly is worse than no gate.

### Tools

Claude Code, Codex CLI, Copilot CLI, Cursor, Gemini CLI and OpenCode. One
method, six recipes: `.daw/` is byte-identical in every installation, and a
recipe declares only where its tool looks for things and what dialect it speaks.

Claude Code and OpenCode have been exercised end to end against the live tool,
and Copilot CLI for three of the four checks — its fourth is detected rather than
prevented, which is that tool's ceiling and not DAW's. Codex CLI, Cursor and
Gemini CLI are verified at the boundary — the test suite drives each tool's real
hook with that tool's own event format — and unverified in the wild.
`scripts/acceptance.md` holds every row, and reports are welcome.

After a compaction, each of the six is reminded to re-read the state and the
phase router instead of answering from the summary. Advisory on all six, which
is what their compaction events are.

### Quality tooling

- `scripts/verify_install.sh` — installs into throwaway repos and drives the
  hooks each tool actually runs.
- `scripts/lint_method.py` — checks the prose against the graph, the rule
  catalog and the filesystem.
- `scripts/mutate.py` — injects known defects one at a time and reports whether
  the suite notices.
- `scripts/check_versions.py` — the version and the licence, read from the files
  that own them and compared against every copy.
- `scripts/check_commits.py` — a range of commits against the attribution rule
  DAW ships: `AI-assisted:` where a model helped, never `Co-Authored-By`.
- CI on Linux and macOS, because a platform difference once removed a large
  share of the checks without changing the exit code.
