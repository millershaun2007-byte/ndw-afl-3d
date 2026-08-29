#!/usr/bin/env bash
# Blocks `git commit` once HEAD is more than MAX_AHEAD commits past the last
# recorded deploy.
#
# Why this exists: on 28-29 Aug 2026 two sessions made 73 and then 14 commits
# with no playtest between them. Five real defects shipped inside that run and
# nobody could tell which change caused what. The rule "one change, one build,
# he plays it" was written in three separate documents and ignored every time,
# because a rule in a file loses to momentum. This is the same rule with teeth.
#
# Clearing it is one command, run after you deploy and Shaun has played it:
#     .claude/hooks/mark-deploy.sh
set -euo pipefail

MAX_AHEAD=2
ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
MARK="$ROOT/.claude/last-deploy"

# No marker yet — let the commit through, but say so once.
[ -f "$MARK" ] || exit 0

LAST=$(tr -d '[:space:]' < "$MARK")
git -C "$ROOT" cat-file -e "${LAST}^{commit}" 2>/dev/null || exit 0

AHEAD=$(git -C "$ROOT" rev-list --count "${LAST}..HEAD" 2>/dev/null || echo 0)
[ "$AHEAD" -le "$MAX_AHEAD" ] && exit 0

SHORT=$(git -C "$ROOT" rev-parse --short "$LAST")
cat <<JSON
{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"deny","permissionDecisionReason":"BLOCKED: $AHEAD commits since the last deploy ($SHORT). The limit is $MAX_AHEAD.\n\nBuild what you have and let Shaun play it before committing again. This repo's whole failure mode is commits stacking up unplayed - 73 of them on 28 Aug, 37 of which were undos.\n\nAfter he has played it, run:  .claude/hooks/mark-deploy.sh\n\nDo not work around this by amending, squashing, or moving the marker without a real deploy."}}
JSON
