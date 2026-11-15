"""Where the wounds in one diag replay came from.

usage: python diagsum.py <diag output>
"""

import collections
import re
import sys
from pathlib import Path


def classify(ctx: str) -> str:
    if "SocialFight" in ctx:
        return "social fight between citizens"
    if re.search(r"AttackMelee\(own\)", ctx):
        return "attacked by an animal of the settlement's own faction"
    if re.search(r"job=(Tame|Train)", ctx):
        return "taming or training an animal"
    if re.search(r"job=AttackStatic target=(Husky|Muffalo|Chicken)", ctx):
        return "hunting (prey fought back)"
    if re.search(r"AttackMelee\(wild\)", ctx):
        return "wild animal attacking, citizen not hunting it"
    return "other/unknown: " + ctx.strip()[:60]


def main() -> None:
    wounds: collections.Counter[str] = collections.Counter()
    infections = 0
    for line in (
        Path(sys.argv[1]).read_text(encoding="utf-8", errors="replace").splitlines()
    ):
        m = re.match(r"d\d+ t\d+ (\S+): \+(\w+)", line)
        if not m or m.group(2) == "BloodLoss":
            continue
        if m.group(2) == "WoundInfection":
            infections += 1
            continue
        wounds[classify(line.split("|", 1)[1])] += 1
    total = sum(wounds.values())
    print("wounds:", total, " wound infections:", infections)
    for kind, n in wounds.most_common():
        print(f"{n:4d} {100 * n / total:5.1f}%  {kind}")


if __name__ == "__main__":
    main()
