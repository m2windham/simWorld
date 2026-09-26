#!/bin/bash
# Lane-private (lane/retreat) mutation proof for the flee trigger. Proves RaidRetreatTests fail with the trigger
# broken and pass with it restored, per CLAUDE.md: the mutation must change the file, the restore must be
# byte-identical, the restored file is touched, and the rebuilt assembly must be newer than the restored source.
#
#   mutate.sh <name> <file relative to worktree> <old string> <new string>
set -u
WT=/home/user/wt-retreat
S=/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-retreat
export PATH=/root/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
NAME=$1; REL=$2; OLD=$3; NEW=$4
F=$WT/$REL
DLL=$WT/artifacts/bin/SimWorld.Core.Tests/debug/SimWorld.Core.dll
OUT=$S/mut; mkdir -p $OUT
ORIG=$OUT/$NAME.orig

cp -p "$F" "$ORIG"
SUM0=$(sha256sum < "$F")

python3 - "$F" "$OLD" "$NEW" <<'PY'
import sys
path, old, new = sys.argv[1], sys.argv[2], sys.argv[3]
text = open(path, encoding="utf-8").read()
n = text.count(old)
if n != 1:
    print("MUTATION ABORT: expected exactly one occurrence, found", n); sys.exit(3)
open(path, "w", encoding="utf-8").write(text.replace(old, new))
PY
[ $? -eq 0 ] || { echo "ABORT: mutation script failed"; exit 3; }
SUM1=$(sha256sum < "$F")
[ "$SUM0" != "$SUM1" ] || { echo "ABORT: mutation did not change the file"; cp -p "$ORIG" "$F"; exit 3; }
echo "[$NAME] mutated $REL"

run() {
  local tag=$1
  dotnet build $WT/tests/SimWorld.Core.Tests/SimWorld.Core.Tests.csproj > $OUT/$NAME-$tag-build.txt 2>&1
  if ! grep -q " 0 Error(s)" $OUT/$NAME-$tag-build.txt; then echo "[$NAME/$tag] BUILD FAILED"; grep -E " error " $OUT/$NAME-$tag-build.txt | head -3; return 1; fi
  dotnet test $WT/tests/SimWorld.Core.Tests/SimWorld.Core.Tests.csproj --no-build --filter "FullyQualifiedName~SimWorld.Tests.AI.RaidRetreatTests" > $OUT/$NAME-$tag-test.txt 2>&1
  local summary; summary=$(grep -E "^(Passed|Failed)!" $OUT/$NAME-$tag-test.txt)
  [ -n "$summary" ] || { echo "[$NAME/$tag] NO SUMMARY LINE - run did not happen"; return 1; }
  echo "[$NAME/$tag] $summary"
  grep -E "^\s+Failed SimWorld" $OUT/$NAME-$tag-test.txt | sed 's/^/    /'
}

run mutated
cp -p "$ORIG" "$F"
SUM2=$(sha256sum < "$F")
[ "$SUM0" = "$SUM2" ] || { echo "ABORT: restore is not byte-identical"; exit 4; }
touch "$F"
echo "[$NAME] restored byte-identical and touched"
run restored
[ "$DLL" -nt "$F" ] || { echo "ABORT: $DLL is not newer than the restored source; the restored run may have tested the mutant"; exit 5; }
echo "[$NAME] rebuilt assembly is newer than the restored source"
