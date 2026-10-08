#!/usr/bin/env bash
# Fetches the three.js r160 files render.js needs into tools/maprender/vendor (git-ignored).
set -euo pipefail
cd "$(dirname "$0")"
base=https://cdn.jsdelivr.net/npm/three@0.160.0
for f in build/three.module.js examples/jsm/loaders/FBXLoader.js examples/jsm/curves/NURBSCurve.js \
         examples/jsm/curves/NURBSUtils.js examples/jsm/libs/fflate.module.js examples/jsm/utils/BufferGeometryUtils.js; do
  mkdir -p "vendor/$(dirname "$f")"
  curl -fsS -o "vendor/$f" "$base/$f"
done
echo "three.js r160 in $(pwd)/vendor"
