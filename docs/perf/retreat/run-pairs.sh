#!/bin/bash
# Lane-private (lane/retreat). Paired continuations of one saved raid, three arms, N continuation seeds.
#   run-pairs.sh <save.xml> <tag> <ticks> <nseeds>
set -u
S=/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-retreat
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
SAVE=$1; TAG=$2; TICKS=$3; N=$4
OUT=$S/pairs/$TAG.txt; : > $OUT
for c in $(seq 1 $N); do
  for arm in nolord lord; do
    dotnet $S/harness2b/bin/Release/net8.0/RetreatPairs.dll cont $SAVE $S/after/src/SimWorld.Core/Data $c $TICKS $arm >> $OUT 2>&1
  done
  dotnet $S/harness2b/bin/Release/net8.0/RetreatPairs.dll cont $SAVE $S/data-autoflee/Data $c $TICKS lord 2>&1 | sed 's/^lord/lord+autoFlee/' >> $OUT
done
echo "done $TAG" >> $OUT
