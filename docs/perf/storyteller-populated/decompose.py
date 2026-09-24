#!/usr/bin/env python3
"""Lane-private (lane/nonsolo). Reads table 1 of a --suite storyteller run and splits the day-0 -> day-N
rise in threat points into log shares, one per factor.

points = (base(w) + perColonist(w) * S) * difficulty * adaptation * era * daysPassed, clamped.
S = colonistSum / perColonist is the health-weighted head count.

ln(P_N / P_0) = ln(pre_N / pre_0) + sum of ln(factor_N / factor_0).
The pre-factor term is split between wealth and population symmetrically (the average of both orders),
so the two shares sum to the pre-factor term exactly and neither is favoured by the order of evaluation.

Shares are of ln(P_N / P_0). Inputs are the table's rounded cells, so a small residual against the
engine's own points column is expected and is printed rather than hidden.
"""
import math
import re
import sys


def table1(path):
    rows = []
    in_t1 = False
    for line in open(path, encoding="utf-8"):
        if line.startswith("### 1. the threat curve"):
            in_t1 = True
            continue
        if in_t1 and line.startswith("### "):
            break
        if in_t1 and line.startswith("| ") and not line.startswith("| day") and not line.startswith("| ---"):
            cells = [c.strip() for c in line.strip().strip("|").split("|")]
            num = [float(c.replace(",", "")) for c in cells[:11]]
            rows.append({
                "day": int(num[0]), "pop": num[1], "wealth": num[2], "base": num[3], "perCol": num[4],
                "colSum": num[5], "diff": num[6], "adapt": num[7], "era": num[8], "days": num[9],
                "points": num[10], "clamp": cells[11],
            })
    if not rows:
        raise SystemExit(path + ": no table 1 found")
    return rows


def f(base, per_col, s):
    return base + per_col * s


def decompose(a, b):
    s_a = a["colSum"] / a["perCol"] if a["perCol"] else 0.0
    s_b = b["colSum"] / b["perCol"] if b["perCol"] else 0.0
    pre_a = f(a["base"], a["perCol"], s_a)
    pre_b = f(b["base"], b["perCol"], s_b)

    w1 = math.log(f(b["base"], b["perCol"], s_a) / pre_a)
    p1 = math.log(pre_b / f(b["base"], b["perCol"], s_a))
    p2 = math.log(f(a["base"], a["perCol"], s_b) / pre_a)
    w2 = math.log(pre_b / f(a["base"], a["perCol"], s_b))

    terms = {
        "wealth": (w1 + w2) / 2,
        "population (health-weighted)": (p1 + p2) / 2,
        "difficulty": math.log(b["diff"] / a["diff"]),
        "adaptation": math.log(b["adapt"] / a["adapt"]),
        "era": math.log(b["era"] / a["era"]),
        "daysPassed": math.log(b["days"] / a["days"]),
    }
    total = math.log(b["points"] / a["points"])
    return terms, total, s_a, s_b


def main():
    for path in sys.argv[1:]:
        rows = table1(path)
        a = rows[0]
        b = rows[-1]
        if b["clamp"] != "-" or b["colSum"] + b["base"] <= 0:
            live = [r for r in rows if r["clamp"] == "-" and r["colSum"] + r["base"] > 0]
            print("## " + path + ": day %d is clamped or empty; decomposing to day %d, the last live sample" % (b["day"], live[-1]["day"]))
            b = live[-1]
        terms, total, s_a, s_b = decompose(a, b)
        explained = sum(terms.values())
        print("## " + path)
        print("day %d -> day %d: points %.1f -> %.1f (x%.2f), ln ratio %.4f" % (
            a["day"], b["day"], a["points"], b["points"], b["points"] / a["points"], total))
        print("pop %d -> %d, S (health-weighted) %.2f -> %.2f, wealth %.0f -> %.0f, clamped days: %d" % (
            a["pop"], b["pop"], s_a, s_b, a["wealth"], b["wealth"], sum(1 for r in rows if r["clamp"] != "-")))
        for name, v in terms.items():
            print("  %-30s ln %+.4f  share %+6.1f%%  factor x%.3f" % (name, v, 100 * v / total, math.exp(v)))
        print("  %-30s ln %+.4f  (rounded cells vs engine column)" % ("residual", total - explained))
        print()


if __name__ == "__main__":
    main()
