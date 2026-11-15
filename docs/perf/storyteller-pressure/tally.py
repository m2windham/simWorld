"""Tally `--suite storyteller` outputs for the storyteller-pressure lane.

usage: python tally.py <dir> [<dir2>]   (dir holds b<band>-s<seed>-<on|off>.txt)

Every threshold below was fixed before the first 60-day run was read.
"""

import re
import statistics
import sys
from dataclasses import dataclass, field
from pathlib import Path

# A big threat "bit" when it killed anybody, or left at least this share of the roster down on a sampled day.
BIT_DOWNED_SHARE = 0.10
# A big threat "mauled" when killed + peak-downed reached this share of the roster it hit.
MAULED_SHARE = 0.30
# A run is a wipe when the roster fell to this share of the founding band or lower.
WIPE_SHARE = 0.25
# Days after a firing over which its peak-downed is read.
DOWNED_WINDOW_DAYS = 2


def num(s: str) -> float:
    return float(s.replace(",", ""))


@dataclass
class Day:
    day: int
    pop: int
    downed: int
    wealth: float
    adaptation: float
    days_factor: float
    points: float


@dataclass
class Firing:
    day: int
    name: str
    category: str
    points: float
    fired: bool
    what: str

    @property
    def killed(self) -> int:
        m = re.search(r"killed (\d+)", self.what)
        return int(m.group(1)) if m else 0

    @property
    def attackers(self) -> int:
        m = re.search(r"(\d+) raiders arrived", self.what) or re.search(
            r"a pack of (\d+)", self.what
        )
        return int(m.group(1)) if m else 0


@dataclass
class Run:
    band: int
    seed: int
    arm: str
    days: list[Day] = field(default_factory=list)
    firings: list[Firing] = field(default_factory=list)
    interesting: dict[str, str] = field(default_factory=dict)
    ledger: str = ""
    moments: list[str] = field(default_factory=list)
    deterministic: bool = True
    ok: bool = False

    def at(self, d: int) -> Day:
        return self.days[min(d, len(self.days) - 1)]

    @property
    def big(self) -> list[Firing]:
        return [f for f in self.firings if f.category == "ThreatBig" and f.fired]

    def pre_pop(self, f: Firing) -> int:
        return self.at(f.day - 1).pop

    def peak_downed_after(self, f: Firing) -> int:
        return max(
            self.at(d).downed
            for d in range(
                f.day, min(f.day + DOWNED_WINDOW_DAYS, len(self.days) - 1) + 1
            )
        )

    @property
    def min_pop(self) -> int:
        return min(d.pop for d in self.days)

    @property
    def end_pop(self) -> int:
        return self.days[-1].pop

    @property
    def deaths(self) -> int:
        return int(self.interesting.get("deaths", "0").replace(",", ""))

    def deaths_by(self, key: str) -> int:
        m = re.search(r"\b" + re.escape(key) + r" (\d+)", self.ledger)
        return int(m.group(1)) if m else 0

    @property
    def threat_deaths(self) -> int:
        return self.deaths_by("RaidEnemy") + self.deaths_by("ManhunterPack")

    def classify(self, f: Firing) -> str:
        p = max(self.pre_pop(f), 1)
        k, d = f.killed, self.peak_downed_after(f)
        if (k + d) / p >= MAULED_SHARE:
            return "mauled"
        if k >= 1 or d >= max(2, BIT_DOWNED_SHARE * p):
            return "bit"
        return "shrug"

    @property
    def verdict(self) -> str:
        if self.min_pop <= WIPE_SHARE * self.band:
            return "WIPE"
        kinds = [self.classify(f) for f in self.big]
        if "mauled" in kinds:
            return "mauled, lived"
        if "bit" in kinds:
            return "bitten"
        return "shrug"


def table_rows(lines: list[str], start: int) -> list[list[str]]:
    rows = []
    i = start
    while i < len(lines) and not lines[i].startswith("|"):
        if lines[i].startswith("###"):
            return rows
        i += 1
    i += 2  # header, separator
    while i < len(lines) and lines[i].startswith("|"):
        rows.append([c.strip() for c in lines[i].strip().strip("|").split("|")])
        i += 1
    return rows


def parse(path: Path) -> Run:
    m = re.match(r"b(\d+)-s(\d+)-(on|off)", path.name)
    assert m, path
    run = Run(int(m.group(1)), int(m.group(2)), m.group(3))
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    run.ok = any(line.strip() == "exit=0" for line in lines[-3:])
    for i, line in enumerate(lines):
        if line.startswith("### 1. the threat curve"):
            for r in table_rows(lines, i + 1):
                run.days.append(
                    Day(
                        int(num(r[0])),
                        int(num(r[1])),
                        int(num(r[2])),
                        num(r[3]),
                        num(r[8]),
                        num(r[10]),
                        num(r[11]),
                    )
                )
        elif line.startswith("### 2. what actually fired"):
            for r in table_rows(lines, i + 1):
                run.firings.append(
                    Firing(int(num(r[0])), r[1], r[2], num(r[3]), r[4] == "yes", r[5])
                )
        elif line.startswith("### was it interesting"):
            header = [c.strip() for c in lines[i + 2].strip().strip("|").split("|")]
            rows = table_rows(lines, i + 1)
            run.interesting = dict(zip(header, rows[0]))
        elif line.startswith("ledger: "):
            run.ledger = line[len("ledger: ") :]
            j = i + 2
            while j < len(lines) and lines[j].startswith("- "):
                run.moments.append(lines[j][2:])
                j += 1
        elif line.startswith("### 4. is it deterministic"):
            run.deterministic = all(r[2] == "yes" for r in table_rows(lines, i + 1))
    return run


def spread(xs: list[float], decimals: int = 0) -> str:
    if not xs:
        return "-"
    fmt = f"{{:.{decimals}f}}"
    if len(xs) == 1:
        return fmt.format(xs[0])
    return f"{fmt.format(statistics.median(xs))} [{fmt.format(min(xs))}..{fmt.format(max(xs))}]"


def per_run(runs: list[Run]) -> None:
    print(
        "| band | seed | arm | pts d0/d15/d30/d45/d60 | big threats (day:kind attackers/pop -> killed, peak downed, class) | deaths (threat-credited) | min pop | end pop | peak downed | moments | verdict |"
    )
    print("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |")
    for r in runs:
        pts = "/".join(f"{r.at(d).points:.0f}" for d in (0, 15, 30, 45, 60))
        bigs = (
            "; ".join(
                f"d{f.day}:{'raid' if f.name == 'RaidEnemy' else 'pack'} {f.attackers}/{r.pre_pop(f)} -> k{f.killed}, dn{r.peak_downed_after(f)}, {r.classify(f)}"
                for f in r.big
            )
            or "none"
        )
        flag = (
            ""
            if r.ok and r.deterministic
            else (" !!INCOMPLETE" if not r.ok else " !!NONDETERMINISTIC")
        )
        print(
            f"| {r.band} | {r.seed} | {r.arm} | {pts} | {bigs} | {r.deaths} ({r.threat_deaths}) | {r.min_pop} | {r.end_pop} | "
            f"{r.interesting.get('peak downed', '?')} | {r.interesting.get('moments', '?')} | {r.verdict if r.arm == 'on' else '-'}{flag} |"
        )


def per_cell(runs: list[Run]) -> None:
    print(
        "| band | arm | n | pts d0 | pts d30 | pts d60 | big threats/run | attackers per defender (all big threats) | killed per big threat | threat class shrug/bit/mauled | deaths/run | of which threat-credited | end pop / band | peak downed / band | moments | verdicts |"
    )
    print(
        "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
    )
    for band in sorted({r.band for r in runs}):
        for arm in ("on", "off"):
            cell = [r for r in runs if r.band == band and r.arm == arm]
            if not cell:
                continue
            bigs = [(r, f) for r in cell for f in r.big]
            ratios = [f.attackers / max(r.pre_pop(f), 1) for r, f in bigs]
            classes = [r.classify(f) for r, f in bigs]
            verdicts: dict[str, int] = {}
            for r in cell:
                verdicts[r.verdict] = verdicts.get(r.verdict, 0) + 1
            print(
                f"| {band} | {arm} | {len(cell)} | {spread([r.at(0).points for r in cell])} | {spread([r.at(30).points for r in cell])} | "
                f"{spread([r.at(60).points for r in cell])} | {spread([float(len(r.big)) for r in cell])} | {spread(ratios, 2)} | "
                f"{spread([float(f.killed) for _, f in bigs], 1)} | {classes.count('shrug')}/{classes.count('bit')}/{classes.count('mauled')} | "
                f"{spread([float(r.deaths) for r in cell])} | {spread([float(r.threat_deaths) for r in cell])} | "
                f"{spread([r.end_pop / band for r in cell], 2)} | {spread([num(r.interesting.get('peak downed', '0')) / band for r in cell], 2)} | "
                f"{spread([num(r.interesting.get('moments', '0')) for r in cell])} | "
                f"{', '.join(f'{k} {v}' for k, v in sorted(verdicts.items())) if arm == 'on' else '-'} |"
            )


def main() -> None:
    for d in sys.argv[1:]:
        runs = sorted(
            (parse(p) for p in Path(d).glob("b*-s*-o*.txt")),
            key=lambda r: (r.band, r.arm != "on", r.seed),
        )
        print(f"\n## {d}\n")
        per_cell(runs)
        print()
        per_run(runs)
        print()
        for r in runs:
            print(f"- b{r.band} s{r.seed} {r.arm}: {r.ledger}")


if __name__ == "__main__":
    main()
