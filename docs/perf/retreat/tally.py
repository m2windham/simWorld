# Lane-private (lane/retreat): tally a paired-continuation file per arm.
import re, sys, statistics as st
rows = {}
for line in open(sys.argv[1]):
    if " | seed " not in line: continue
    arm = line.split(" | ")[0]
    g = lambda pat: re.search(pat, line)
    hold = g(r"hold >?(\d+)"); d = g(r"end dead/down/left/standing (\d+)/(\d+)/(\d+)/(\d+)")
    rs = g(r"standing again after clear (\d+)"); ac = g(r"downings after clear (\d+)")
    dh = g(r"downings held/all (\d+)/(\d+)"); de = g(r"deaths at clear (-|\d+), all (\d+)")
    let = g(r"letter \+(\d+)")
    rows.setdefault(arm, []).append(dict(hold=int(hold.group(1)), dead=int(d.group(1)), down=int(d.group(2)), left=int(d.group(3)),
        restand=int(rs.group(1)) if rs else 0, dafter=int(ac.group(1)) if ac else 0, dheld=int(dh.group(1)), dall=int(dh.group(2)),
        deaths=int(de.group(2)), letter=int(let.group(1)) if let else None))
print("arm | n | hold median (min-max) | letter median | citizen downings held / all (sum) | citizen deaths (sum) | raiders dead/left at end (sum) | raider re-standing after clear, ticks (sum)")
for arm, rs in rows.items():
    holds = [r["hold"] for r in rs]; lets = [r["letter"] for r in rs if r["letter"] is not None]
    print(f"{arm} | {len(rs)} | {int(st.median(holds))} ({min(holds)}-{max(holds)}) | {int(st.median(lets)) if lets else '-'} | "
          f"{sum(r['dheld'] for r in rs)} / {sum(r['dall'] for r in rs)} | {sum(r['deaths'] for r in rs)} | "
          f"{sum(r['dead'] for r in rs)} / {sum(r['left'] for r in rs)} | {sum(r['restand'] for r in rs)}")
