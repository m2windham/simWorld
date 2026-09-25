#!/usr/bin/env python3
# Lane-private (lane/tend) mutation proof. For each mutation: back up the file, apply the mutation and ASSERT it
# changed the file, rebuild, run the target tests, restore byte-identically, TOUCH the file, rebuild, confirm the
# rebuilt DLL is newer than the restored source, run the tests again. Any mutation that does not change the file
# aborts. Run only while nothing else builds or tests in /home/user/wt-tend.
import hashlib, os, shutil, subprocess, sys, time, re

WT = "/home/user/wt-tend"
LANE = "/tmp/claude-0/-home-user-simWorld/6fdaa363-1092-5a74-9175-8c28a59788fa/scratchpad/lane-tend/mut"
ENV = dict(os.environ, PATH="/root/.dotnet:" + os.environ["PATH"], DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
TESTS = "tests/SimWorld.Core.Tests/SimWorld.Core.Tests.csproj"
DLL = WT + "/artifacts/bin/SimWorld.Core/debug/SimWorld.Core.dll"
FILTER = "FullyQualifiedName~EmergencyWorkTests|FullyQualifiedName~SimWorld.Tests.AI.AITests.Humanlike_think_tree|FullyQualifiedName~DoctorAITests"

def sha(p): return hashlib.sha256(open(p, "rb").read()).hexdigest()

def run(cmd, log):
    with open(log, "w") as f:
        r = subprocess.run(cmd, cwd=WT, env=ENV, stdout=f, stderr=subprocess.STDOUT)
    return r.returncode, open(log).read()

def build(tag):
    code, out = run(["dotnet", "build", TESTS], f"{LANE}/{tag}-build.log")
    if code != 0 or "Build succeeded" not in out: sys.exit(f"{tag}: build failed, see {LANE}/{tag}-build.log")

def test(tag):
    code, out = run(["dotnet", "test", TESTS, "--no-build", "--filter", FILTER], f"{LANE}/{tag}-test.log")
    summary = [l for l in out.splitlines() if l.strip().startswith(("Passed!", "Failed!"))]
    if not summary: sys.exit(f"{tag}: no Passed!/Failed! summary line — the run did not happen")
    fails = sorted(set(re.findall(r"(\S+) \[FAIL\]", out)))
    return summary[0].strip(), fails

MUTATIONS = {
    "M1-no-emergency-tier": ("src/SimWorld.Core/Data/Core/Defs/ThinkTreeDefs/ThinkTrees_Humanlike.xml",
        lambda s: s.replace('        <li Class="SimWorld.AI.JobGiver_Work">\n          <emergency>true</emergency>\n        </li>\n', '', 1)),
    "M2-no-sleep-lookup": ("src/SimWorld.Core/AI/JobDriver_LayDown.cs",
        lambda s: s.replace("if (pawn.IsHashIntervalTick(LookForOtherJobsIntervalTicks))", "if (false && pawn.IsHashIntervalTick(LookForOtherJobsIntervalTicks))", 1)),
    "M3-old-urgency-rule": ("src/SimWorld.Core/Health/TendUtility.cs",
        lambda s: s.replace("public static bool NeedsEmergencyTend(Pawn patient) => HealthAIUtility.ShouldBeTendedNowUrgent(patient);",
            "public static bool NeedsEmergencyTend(Pawn patient) { foreach (Hediff h in patient.health.hediffSet.hediffs) { if (!h.TendableNow()) continue; if (h.BleedRate > 0f) return true; HediffStage? st = h.CurStage; if (st != null && st.lifeThreatening) return true; } return false; }", 1)),
}

which = sys.argv[1:] or list(MUTATIONS)
for name in which:
    rel, mutate = MUTATIONS[name]
    path = f"{WT}/{rel}"
    backup = f"{LANE}/{name}.orig"
    shutil.copy2(path, backup)
    before = sha(path)
    src = open(path, encoding="utf-8").read()
    mutated = mutate(src)
    if mutated == src: sys.exit(f"{name}: mutation did not change {rel} — aborting")
    open(path, "w", encoding="utf-8").write(mutated)
    assert sha(path) != before, f"{name}: file unchanged after mutation"
    try:
        build(f"{name}-mut")
        if rel.endswith(".xml"):
            copy = WT + "/artifacts/bin/SimWorld.Core.Tests/debug/" + rel.split("SimWorld.Core/", 1)[1]
            if sha(copy) != sha(path): sys.exit(f"{name}: the test output's copy does not carry the mutation")
        elif os.path.getmtime(DLL) <= os.path.getmtime(path): sys.exit(f"{name}: mutated DLL is not newer than the mutated source")
        s1, f1 = test(f"{name}-mut")
        print(f"{name} MUTATED : {s1}")
        for t in f1: print(f"    fails: {t}")
    finally:
        shutil.copyfile(backup, path)          # contents only; mtime handled below
        if sha(path) != before: sys.exit(f"{name}: restore not byte-identical — STOP, {rel} is dirty")
        os.utime(path, None)                   # touch so the build cannot skip it
    build(f"{name}-restored")
    if rel.endswith(".xml"):
        # Content is not compiled: the test host loads the copy the build puts next to the test assembly.
        copy = WT + "/artifacts/bin/SimWorld.Core.Tests/debug/" + rel.split("SimWorld.Core/", 1)[1]
        if sha(copy) != before: sys.exit(f"{name}: the test output's copy of {rel} is not the restored file")
    elif os.path.getmtime(DLL) <= os.path.getmtime(path): sys.exit(f"{name}: rebuilt DLL is not newer than the restored source")
    s2, f2 = test(f"{name}-restored")
    print(f"{name} RESTORED: {s2}" + ("" if not f2 else "  FAILS: " + ", ".join(f2)))
