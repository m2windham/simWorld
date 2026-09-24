#!/usr/bin/env bash
# Lane-private (lane/downed). Continues one save under the old and the new infection model, per continuation
# seed, the two arms side by side. Usage: run-pairs.sh <worldSeed> <save.xml> <endDay> <contSeed>...
set -uo pipefail
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
L=/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-downed
SEED=$1; SAVE=$2; END=$3; shift 3
mkdir -p "$L/pairs"
for c in "$@"; do
  dotnet "$L/pair-before/bin/Release/net8.0/Pair.dll" load "$SEED" "$SAVE" "$L/data-before" "$END" "$c" > "$L/pairs/w$SEED-c$c-before.txt" 2>&1 &
  B=$!
  dotnet "$L/pair-after/bin/Release/net8.0/Pair.dll" load "$SEED" "$SAVE" "$L/data-after" "$END" "$c" > "$L/pairs/w$SEED-c$c-after.txt" 2>&1 &
  A=$!
  wait $B; wait $A
  for arm in before after; do
    echo "w$SEED c$c $arm: $(grep -E '^PAIR|LOAD ERRORS|Exception' "$L/pairs/w$SEED-c$c-$arm.txt" | head -2)"
  done
done
