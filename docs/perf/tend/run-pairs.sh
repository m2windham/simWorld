#!/usr/bin/env bash
# Lane-private (lane/tend). Continues one save under the before arm (a685640's code and content) and the after
# arm (lane/tend's), per continuation seed, one run at a time. Usage: run-pairs.sh <save.xml> <endDay> <contSeed>...
set -uo pipefail
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
L=/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-tend
SAVE=$1; END=$2; shift 2
TAG=$(basename "$SAVE" .xml)
mkdir -p "$L/pairs"
for c in "$@"; do
  for arm in before after; do
    dotnet "$L/diag-$arm/TendDiag.dll" load "$SAVE" "$L/data-$arm/Data" "$END" "$c" > "$L/pairs/$TAG-c$c-$arm.txt" 2>&1
    echo "$TAG c$c $arm exit $? $(date)" >> "$L/pairs/progress.log"
  done
done
