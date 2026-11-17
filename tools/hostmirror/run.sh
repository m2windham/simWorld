#!/usr/bin/env bash
# Runs the layout bench on Unity's Mono, the runtime the host plays on (see HostMirror.cs for why that differs).
# Usage: tools/hostmirror/run.sh [--layout-ticks N] [--layout-every N] [--layout-digest-every N] ...
# UNITY_MONO points at the Editor's MonoBleedingEdge folder; OUT at a scratch folder for the build.
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
repo="$(cd "$here/../.." && pwd)"
mono_root="${UNITY_MONO:-/h/tools/Unity/6000.5.0f1/Editor/Data/MonoBleedingEdge}"
lib="$mono_root/lib/mono/unityjit-win32"
out="${OUT:-$repo/artifacts/hostmirror}"
csc="$(dirname "$(command -v dotnet)")/sdk/$(dotnet --version)/Roslyn/bincore/csc.dll"
mkdir -p "$out"

refs=(-r:"$lib/mscorlib.dll" -r:"$lib/System.dll" -r:"$lib/System.Core.dll" -r:"$lib/System.Xml.dll"
      -r:"$lib/System.Xml.Linq.dll" -r:"$lib/Facades/netstandard.dll")
win() { if command -v cygpath >/dev/null; then cygpath -w "$1"; else echo "$1"; fi; }

# Unity compiles the package at C# 9 against its own class libraries; so does this. Nullable analysis is off:
# those libraries carry no annotations, so it only warns, and it never changes the code generated.
find "$repo/src/SimWorld.Core" -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*' | while read -r f; do win "$f"; done > "$out/core.rsp"
dotnet "$csc" -nologo -langversion:9 -nullable:disable -nowarn:CS8632 -optimize+ -nostdlib -target:library \
  -out:"$out/SimWorld.Core.dll" "${refs[@]}" @"$out/core.rsp"

bench="$repo/tools/bench/SimWorld.Bench"
dotnet "$csc" -nologo -langversion:9 -nullable:disable -nowarn:CS8632 -optimize+ -nostdlib -target:exe \
  -out:"$out/HostMirror.exe" "${refs[@]}" -r:"$out/SimWorld.Core.dll" \
  "$here/HostMirror.cs" "$bench/Suites/LayoutSuite.cs" "$bench/BenchOptions.cs" "$bench/Report.cs"

export SIMWORLD_DATA="$(win "$repo/src/SimWorld.Core/Data")"
exec "$mono_root/bin/mono.exe" "$out/HostMirror.exe" "$@"
