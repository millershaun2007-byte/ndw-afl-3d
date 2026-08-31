#!/usr/bin/env bash
# Upload a Unity WebGL build to gs://ndw-game-builds/<name>/ correctly.
#
# Exists because on 2026-08-28 AFL was "broken" for days and the cause was
# none of the things we chased. The build URLs in Unity's index.html are bare
# and never change between builds:
#
#     dataUrl: "Build/WebGL.data"
#
# Unity caches .data/.wasm in IndexedDB keyed by that URL, so any browser that
# had ever loaded the game kept replaying its cached copy no matter what we
# uploaded. HTTP no-cache does not reach IndexedDB. Every rebuild since 17 Aug
# was invisible to a returning player.
#
# The stamp is derived from the build's own bytes, so it changes when and only
# when the build changes - re-uploading identical bytes does not force players
# to re-download 47MB.
#
# Usage:  ./sync-to-gcs.sh afl3d [path/to/Build/WebGL]
set -euo pipefail

NAME="${1:?usage: sync-to-gcs.sh <gcs-folder-name> [build-dir]}"
DIR="${2:-Build/WebGL}"
DEST="gs://ndw-game-builds/$NAME"

[ -d "$DIR" ] || { echo "no build dir: $DIR"; exit 1; }
[ -f "$DIR/index.html" ] || { echo "no index.html in $DIR"; exit 1; }

STAMP=$(cat "$DIR/Build/WebGL.wasm" "$DIR/Build/WebGL.data" | md5 -q | cut -c1-10)
echo "content stamp: $STAMP"

TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
cp -R "$DIR"/. "$TMP/"
sed -i '' \
  -e "s|\"Build/WebGL\.loader\.js\"|\"Build/WebGL.loader.js?v=$STAMP\"|" \
  -e "s|\"Build/WebGL\.data\"|\"Build/WebGL.data?v=$STAMP\"|" \
  -e "s|\"Build/WebGL\.framework\.js\"|\"Build/WebGL.framework.js?v=$STAMP\"|" \
  -e "s|\"Build/WebGL\.wasm\"|\"Build/WebGL.wasm?v=$STAMP\"|" \
  "$TMP/index.html"
grep -q "?v=$STAMP" "$TMP/index.html" || { echo "cache-bust did not apply - index.html format changed"; exit 1; }

# cp, never rsync: -m rsync hangs indefinitely on this Mac (macOS python
# multiprocessing fork issue), documented in CLAUDE.md.
gsutil -m cp -r "$TMP"/* "$DEST/" >/dev/null
gsutil -h "Cache-Control:no-cache, max-age=0" cp "$TMP/index.html" "$DEST/index.html" >/dev/null

# Verify per file, byte for byte. Never trust an aggregate size - on 2026-08-28
# local and live were both "47MB" and differed by 565 bytes of .wasm.
fail=0
for f in WebGL.wasm WebGL.data WebGL.framework.js WebGL.loader.js; do
  want=$(stat -f%z "$DIR/Build/$f")
  got=$(gsutil stat "$DEST/Build/$f" | awk '/Content-Length/{print $2}')
  if [ "$want" = "$got" ]; then printf '  OK   %-20s %s\n' "$f" "$got"
  else printf '  FAIL %-20s local=%s live=%s\n' "$f" "$want" "$got"; fail=1; fi
done
[ "$fail" = 0 ] && echo "synced: $DEST (v=$STAMP)" || { echo "SYNC INCOMPLETE"; exit 1; }
