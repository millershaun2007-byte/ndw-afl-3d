#!/usr/bin/env bash
# Record the commit that was just deployed AND played. Clears the commit gate.
# Only run this after a real build reached Shaun - not after a build, not after
# a push. The gate is worthless if the marker moves without a playtest.
set -euo pipefail
ROOT=$(git rev-parse --show-toplevel)
git -C "$ROOT" rev-parse HEAD > "$ROOT/.claude/last-deploy"
echo "deploy marker set to $(git -C "$ROOT" rev-parse --short HEAD) - commit gate cleared"
