#!/bin/bash
# usage: run-one.sh <bin-dir> <out-dir> <band> <seed> <threats>
bin=$1; out=$2; band=$3; seed=$4; threats=$5
arm=on; [ "$threats" = "false" ] && arm=off
f="$out/b${band}-s${seed}-${arm}.txt"
[ -e "$f" ] || [ -e "$f.part" ] && exit 0
cd "$bin" && ./SimWorld.Bench.exe --suite storyteller --solo false --days 60 --seed "$seed" --band "$band" --threats "$threats" > "$f.part" 2>&1
echo "exit=$?" >> "$f.part"
mv "$f.part" "$f"
