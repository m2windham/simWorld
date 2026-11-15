#!/bin/bash
# usage: run-at.sh <commit> <seed> <days> <band> <threats>
# Builds SimWorld.Core as it stood at <commit> (already extracted under bisect/<commit>/tree), builds the
# lane diagnostic against that binary, and replays the storyteller world with it.
set -u
here="$(cd "$(dirname "$0")" && pwd)"
c=$1; seed=$2; days=$3; band=$4; threats=$5
tree="$here/$c/tree"
out="$here/$c/diag-b${band}-s${seed}-$([ "$threats" = "false" ] && echo off || echo on).txt"
{
  (cd "$tree" && dotnet build src/SimWorld.Core/SimWorld.Core.csproj -c Release 2>&1 | tail -3)
  dll="$tree/artifacts/bin/SimWorld.Core/release/SimWorld.Core.dll"
  [ -f "$dll" ] || { echo "NO CORE DLL at $c"; exit 1; }
  mkdir -p "$here/$c/diag" && cp "$here/../diag/diag.cs" "$here/$c/diag/" && cp "$here/DiagAt.csproj" "$here/$c/diag/"
  (cd "$here/$c/diag" && dotnet build DiagAt.csproj -c Release -p:CoreDll="$(cygpath -w "$dll")" -o "$here/$c/diag/bin" 2>&1 | grep -E "error|Build succeeded" | sort -u | cut -c1-300)
  [ -f "$here/$c/diag/bin/PressureDiag.exe" ] || { echo "NO DIAG EXE at $c"; exit 1; }
} > "$here/$c/build.log" 2>&1
"$here/$c/diag/bin/PressureDiag.exe" "$seed" "$days" "$band" "$threats" "$tree/src/SimWorld.Core/Data" > "$out" 2>&1
echo "exit=$?" >> "$out"
