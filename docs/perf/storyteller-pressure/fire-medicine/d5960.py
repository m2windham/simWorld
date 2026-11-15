import re, sys
from pathlib import Path
def pops(path):
    out = {}
    in_table = False
    for line in Path(path).read_text(encoding='utf-8', errors='replace').splitlines():
        if line.startswith('| day | pop |'):
            in_table = True; continue
        if in_table:
            if not line.startswith('|'):
                if out: break
                continue
            cells = [c.strip() for c in line.strip('|').split('|')]
            if cells[0].isdigit(): out[int(cells[0])] = cells[1]
    return out
for d in sys.argv[1:]:
    for f in sorted(Path(d).glob('b*-off.txt')):
        p = pops(f)
        print(f"{d}\t{f.stem}\t{p.get(59,'-')}\t{p.get(60,'-')}")
