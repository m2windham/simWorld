#!/usr/bin/env python3
# Lane-private (lane/tend). Pools the paired continuations per save and arm.
import sys, glob, re, collections
L = sys.argv[1]
def q(xs, p):
    xs = sorted(xs); return xs[min(len(xs)-1, int(p*(len(xs)-1)+0.5))] if xs else None
def parse(path):
    rows = []; inside = False; doing = {}
    for line in open(path):
        if line.startswith("EPISODES"): inside = True; continue
        if inside and line.startswith("pawn |"): continue
        if inside and not line.strip(): inside = False; continue
        if inside: rows.append([c.strip() for c in line.split("|")]); continue
        m = re.match(r"\s+(hostile standing on map|map clear) \((\d+) samples\): (.*)", line)
        if m:
            d = dict((k, int(v)) for k, v in (kv.rsplit("=", 1) for kv in m.group(3).split(", ") if "=" in kv))
            doing[m.group(1)] = (int(m.group(2)), d)
    return rows, doing
for save in sorted(set(re.sub(r"-c\d+-(before|after)\.txt$", "", p) for p in glob.glob(L + "/pairs/*-c*-*.txt"))):
    for arm in ("before", "after"):
        files = sorted(glob.glob(f"{save}-c*-{arm}.txt"))
        down = bleed = never = died_bl = died_other = 0; ftj = []; per = []; asleep = fight = tend = samp = 0
        for f in files:
            rows, doing = parse(f)
            b = [r for r in rows if float(r[2]) > 0]
            down += len(rows); bleed += len(b); never += sum(1 for r in b if r[9] == "-")
            ftj += [int(r[9][1:]) for r in b if r[9].startswith("+")]
            dbl = sum(1 for r in rows if r[13] == "died" and r[15] == "BloodLoss"); died_bl += dbl; per.append(dbl)
            died_other += sum(1 for r in rows if r[13] == "died" and r[15] != "BloodLoss")
            for k, (n, d) in doing.items():
                for job, c in d.items():
                    total = c
                    if "asleep" in job: asleep += total
                    elif job.startswith("Attack"): fight += total
                    elif job.startswith("TendPatient"): tend += total
                samp += sum(d.values())
        print(f"{save.split('/')[-1]} {arm:6s} seeds={len(files)} downings={down} bleeding={bleed} bleeding-never-tend-job={never} "
              f"firstTendJob p50/p75/p90/max={q(ftj,.5)}/{q(ftj,.75)}/{q(ftj,.9)}/{max(ftj) if ftj else None} "
              f"bledOut={died_bl} perSeed={per} otherDeaths={died_other} | while someone bled untended, able-citizen samples: "
              f"asleep {asleep}, fighting {fight}, tending {tend} of {samp}")
