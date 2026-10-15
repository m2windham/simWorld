#!/usr/bin/env python3
"""Lane-private mutation prover (lane/stranded). CLAUDE.md's rules, enforced:
 - the mutation must change the file (sha256) or the run aborts;
 - the restore must be byte-identical (sha256 equals the pre-mutation hash);
 - after the restore the file is touched; a .cs restore is trusted only if the rebuilt assembly is newer than
   it, an .xml restore only if the copy the build shipped beside the tests is byte-identical to it;
 - a test run counts only if its Passed!/Failed! summary line is present (never an exit code through a pipe).
usage: mutate.py <label> <relative file> <old> <new> <filter>
"""
import hashlib, os, shutil, subprocess, sys, time, re

ROOT = "/home/user/wt-stranded"
PRIV = "/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-stranded/mutation"
os.makedirs(PRIV, exist_ok=True)
DLL = ROOT + "/artifacts/bin/SimWorld.Core/debug/SimWorld.Core.dll"
TESTDATA = ROOT + "/artifacts/bin/SimWorld.Core.Tests/debug/Data/Core/Defs"
ENV = dict(os.environ, PATH="/root/.dotnet:" + os.environ["PATH"], DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")

def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest()

def run_tests(flt, log):
    r = subprocess.run(["dotnet", "test", ROOT + "/tests/SimWorld.Core.Tests/SimWorld.Core.Tests.csproj", "--filter", flt],
                       cwd=ROOT, env=ENV, capture_output=True, text=True, timeout=1700)
    out = r.stdout + r.stderr
    open(log, "w").write(out)
    summary = [l for l in out.splitlines() if re.search(r"^(Passed|Failed)!", l.strip())]
    failed = sorted(set(re.findall(r"^\s+Failed (\S+)", out, re.M)))
    return summary, failed, out

label, rel, old, new, flt = sys.argv[1:6]
path = os.path.join(ROOT, rel)
backup = os.path.join(PRIV, label + ".orig")
before = sha(path)
shutil.copyfile(path, backup)
assert sha(backup) == before

text = open(path, encoding="utf-8").read()
assert text.count(old) == 1, "mutation anchor must occur exactly once, found %d" % text.count(old)
open(path, "w", encoding="utf-8", newline="").write(text.replace(old, new))
mutated = sha(path)
if mutated == before:
    shutil.copyfile(backup, path)
    sys.exit("ABORT: the mutation did not change the file")
print("[%s] mutated %s  %s -> %s" % (label, rel, before[:12], mutated[:12]), flush=True)

try:
    summary, failed, _ = run_tests(flt, os.path.join(PRIV, label + ".mutated.log"))
finally:
    shutil.copyfile(backup, path)
after = sha(path)
if after != before:
    sys.exit("ABORT: restore is not byte-identical (%s != %s)" % (after[:12], before[:12]))
print("[%s] restored byte-identical %s" % (label, after[:12]), flush=True)

if not summary:
    sys.exit("ABORT: the mutated run printed no Passed!/Failed! summary; it did not happen")
print("[%s] MUTATED RUN: %s" % (label, summary[0].strip()), flush=True)
for f in failed:
    print("[%s]   failed: %s" % (label, f), flush=True)
if not failed:
    sys.exit("ABORT: the mutation was NOT caught by any test")

time.sleep(1.1)
os.utime(path, None)  # touch: the restored source must look newer than the mutated build's output
summary2, failed2, _ = run_tests(flt, os.path.join(PRIV, label + ".restored.log"))
if rel.endswith(".cs"):
    src_m, dll_m = os.path.getmtime(path), os.path.getmtime(DLL)
    if not dll_m > src_m:
        sys.exit("ABORT: rebuilt assembly (%f) is not newer than the restored source (%f)" % (dll_m, src_m))
    print("[%s] rebuilt assembly newer than restored source: yes (dll %.1fs after)" % (label, dll_m - src_m), flush=True)
else:
    shipped = os.path.join(TESTDATA, os.path.relpath(path, ROOT + "/src/SimWorld.Core/Data/Core/Defs"))
    if sha(shipped) != sha(path):
        sys.exit("ABORT: the content copy beside the tests (%s) is not the restored file" % shipped)
    print("[%s] content copy beside the tests is byte-identical to the restored source: yes" % label, flush=True)
if not summary2:
    sys.exit("ABORT: the restored run printed no summary line")
print("[%s] RESTORED RUN: %s" % (label, summary2[0].strip()), flush=True)
if failed2:
    sys.exit("ABORT: restored run has failures: %s" % failed2)
