"""What each non-threat incident was followed by: roster the day before it fired and two days after.

usage: python storms.py <dir> [incident defName ...]   (default: Flashstorm)
"""

import sys
from pathlib import Path

from tally import parse


def main() -> None:
    names = sys.argv[2:] or ["Flashstorm"]
    runs = sorted(
        (parse(p) for p in Path(sys.argv[1]).glob("b*-s*-o*.txt")),
        key=lambda r: (r.band, r.arm != "on", r.seed),
    )
    print(
        "| band | seed | arm | incident | day | roster the day before | roster two days after | lost |"
    )
    print("| --- | --- | --- | --- | --- | --- | --- | --- |")
    for r in runs:
        for f in r.firings:
            if f.name not in names or not f.fired:
                continue
            before = r.at(f.day - 1).pop
            after = r.at(f.day + 2).pop
            if before == 0:
                continue
            print(
                f"| {r.band} | {r.seed} | {r.arm} | {f.name} | {f.day} | {before} | {after} | {before - after} |"
            )


if __name__ == "__main__":
    main()
