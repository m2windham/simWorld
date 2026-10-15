#!/usr/bin/env python3
"""Pull the comparison rows out of two storyteller reports. usage: extract.py <report.md> [<report.md> ...]"""
import re, sys
for p in sys.argv[1:]:
    t = open(p).read()
    def section(name):
        m = re.search(r"### " + name + r".*?\n(.*?)(?=\n### |\Z)", t, re.S)
        return m.group(1) if m else ""
    thr = [l for l in section("throughput").splitlines() if l.startswith("| 60")]
    wii = [l for l in section("was it interesting").splitlines() if re.match(r"\| \d", l)]
    det = [l for l in section("4. is it deterministic").splitlines() if l.startswith("|") and ("yes" in l or "NO" in l or "no" in l.split("|")[-2])]
    fired = [l for l in section("2. what actually fired").splitlines() if "RaidEnemy" in l]
    print("==", p)
    print("throughput :", thr)
    print("interesting:", wii)
    for d in det: print("determinism:", d)
    for f in fired: print("raid       :", f)
