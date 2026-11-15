"""The storyteller-pressure lane's headline table: threats on beside threats off, per band.

usage: python report.py <dir>

Reads the same files tally.py does. A big threat counts as having landed on a settlement only while at
least half the founding band was still alive; one that arrives after that is reported separately, because a
raid on three survivors says nothing about the curve.
"""

import statistics
import sys
from pathlib import Path

from tally import Firing, Run, parse

LIVE_SHARE = 0.5


def spread(xs: list[float], decimals: int = 0) -> str:
    if not xs:
        return "-"
    fmt = f"{{:.{decimals}f}}"
    if len(xs) == 1:
        return fmt.format(xs[0])
    return f"{fmt.format(statistics.median(xs))} [{fmt.format(min(xs))}..{fmt.format(max(xs))}]"


def first_day_at_or_below(run: Run, share: float) -> float:
    for d in run.days:
        if d.pop <= share * run.band:
            return float(d.day)
    return float("inf")


def day_text(xs: list[float]) -> str:
    finite = [x for x in xs if x != float("inf")]
    never = len(xs) - len(finite)
    text = spread(finite) if finite else "-"
    return text + (f" ({never} never)" if never else "")


def live(run: Run, f: Firing) -> bool:
    return run.pre_pop(f) >= LIVE_SHARE * run.band


def main() -> None:
    runs = sorted(
        (parse(p) for p in Path(sys.argv[1]).glob("b*-s*-o*.txt")),
        key=lambda r: (r.band, r.arm != "on", r.seed),
    )
    bad = [r for r in runs if not r.ok or not r.deterministic]
    print(
        f"runs: {len(runs)}; incomplete or non-deterministic: {[(r.band, r.seed, r.arm) for r in bad]}\n"
    )

    print(
        "| band | arm | seeds | day half the band was dead | day roster <= 25% | extinct by day 60 | deaths/run | credited to raid or pack | raids+packs on a live settlement, per run | attackers per defender | killed per such threat | after collapse, per run | points d0 / d10 / d20 | moments |"
    )
    print(
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
    )
    for band in sorted({r.band for r in runs}):
        for arm in ("on", "off"):
            cell = [r for r in runs if r.band == band and r.arm == arm]
            if not cell:
                continue
            lives = [(r, f) for r in cell for f in r.big if live(r, f)]
            print(
                f"| {band} | {arm} | {len(cell)} | {day_text([first_day_at_or_below(r, 0.5) for r in cell])} | "
                f"{day_text([first_day_at_or_below(r, 0.25) for r in cell])} | "
                f"{sum(1 for r in cell if r.min_pop == 0)}/{len(cell)} | {spread([float(r.deaths) for r in cell])} | "
                f"{spread([float(r.threat_deaths) for r in cell])} | "
                f"{spread([float(sum(1 for f in r.big if live(r, f))) for r in cell])} | "
                f"{spread([f.attackers / r.pre_pop(f) for r, f in lives], 2)} | {spread([float(f.killed) for _, f in lives], 1)} | "
                f"{spread([float(sum(1 for f in r.big if not live(r, f))) for r in cell])} | "
                f"{spread([r.at(0).points for r in cell])} / {spread([r.at(10).points for r in cell])} / {spread([r.at(20).points for r in cell])} | "
                f"{spread([float(r.interesting.get('moments', '0')) for r in cell])} |"
            )

    print()
    print("| band | seed | arm | pop at day 0/5/10/15/20/30/45/60 | ledger |")
    print("| --- | --- | --- | --- | --- |")
    for r in runs:
        pops = "/".join(str(r.at(d).pop) for d in (0, 5, 10, 15, 20, 30, 45, 60))
        print(f"| {r.band} | {r.seed} | {r.arm} | {pops} | {r.ledger} |")

    print()
    print(
        "| band | seed | raids and packs that found at least half the band alive (day: kind attackers/defenders at points -> killed, peak downed within 2 days) |"
    )
    print("| --- | --- | --- |")
    for r in runs:
        if r.arm != "on":
            continue
        text = "; ".join(
            f"d{f.day}: {'raid' if f.name == 'RaidEnemy' else 'pack'} {f.attackers}/{r.pre_pop(f)} at {f.points:.0f} -> k{f.killed}, dn{r.peak_downed_after(f)}"
            for f in r.big
            if live(r, f)
        )
        print(f"| {r.band} | {r.seed} | {text or 'none'} |")


if __name__ == "__main__":
    main()
