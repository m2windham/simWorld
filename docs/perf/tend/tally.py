#!/usr/bin/env python3
# Lane-private (lane/tend). Tallies diag outputs: downings, time to first tend job, bleed-out deaths.
# Usage: tally.py [--from TICK] [--to TICK] file...
import sys
def q(xs, p):
    xs = sorted(xs); return xs[min(len(xs)-1, int(p*(len(xs)-1)+0.5))] if xs else None
def tick(stamp):
    d, t = stamp.split("."); return int(d) * 60000 + int(t)
args = sys.argv[1:]; lo, hi = 0, 10**12
while args and args[0].startswith("--"):
    if args[0] == "--from": lo = int(args[1])
    if args[0] == "--to": hi = int(args[1])
    args = args[2:]
for path in args:
    rows = []; inside = False
    for line in open(path):
        if line.startswith("EPISODES"): inside = True; continue
        if inside and line.startswith("pawn |"): continue
        if inside and not line.strip(): break
        if inside: rows.append([c.strip() for c in line.split("|")])
    rows = [r for r in rows if lo <= tick(r[1]) < hi]
    bleeding = [r for r in rows if float(r[2]) > 0]
    urgent = [r for r in rows if r[4] != "-" and int(r[4]) < 45000]
    ftj = [int(r[9][1:]) for r in bleeding if r[9].startswith("+")]
    never = sum(1 for r in bleeding if r[9] == "-")
    deaths = [r for r in rows if r[13] == "died"]
    bl = sum(1 for r in deaths if r[15] == "BloodLoss")
    other = {}
    for r in deaths:
        if r[15] != "BloodLoss": other[r[15]] = other.get(r[15], 0) + 1
    print(f"{path.split('/')[-1]}: downings {len(rows)}, bleeding {len(bleeding)} (urgent at downing {len(urgent)}), "
          f"bleeding with a tend job {len(ftj)}, never {never}; first tend job p25/median/p75/p90/max "
          f"{q(ftj,.25)}/{q(ftj,.5)}/{q(ftj,.75)}/{q(ftj,.9)}/{max(ftj) if ftj else None}; "
          f"died of blood loss {bl}, other deaths {other}")
