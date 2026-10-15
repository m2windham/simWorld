#!/bin/bash
# lane/stranded: runs seeds 777 then 12345, one after the other. usage: run-bench.sh <bench dir> <tag>
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
S=/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-stranded
DIR=$1; TAG=$2
echo $$ > "$S/$TAG.pid"
TIMEFORMAT="wall=%R user=%U sys=%S"
for SEED in 777 12345; do
  cd "$DIR"
  { time dotnet SimWorld.Bench.dll --suite storyteller --solo false --days 60 --seed $SEED > "$S/$TAG-$SEED.md" 2> "$S/$TAG-$SEED.err" ; } 2> "$S/$TAG-$SEED.time"
  echo "seed $SEED exit $?" >> "$S/$TAG.done"
done
echo finished >> "$S/$TAG.done"
