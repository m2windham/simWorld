# Epoch — inspiration for SimWorld's progression systems

Epoch is Doug's other project: a deterministic Rust/Bevy settlement-and-eras game
(`crates/epoch_core/src/sim.rs`, 12.9k lines) that carries a single settlement from
"Thatch & Sinew" toward "Wire & Light" — six authored epochs, four eras each, an
authored 78-row tech table, a Knowledge economy that feeds it, and a Great Works /
lineage / Historian layer that is specced in exhaustive detail but only partly built.
It is worth reading closely for three reasons that have nothing to do with genre
similarity alone:

- **Same author, same design taste.** The invariants, the vocabulary ("charter",
  "era", "epoch"), and the aesthetic rule ("no choice may feel hollow") are the same
  hand that is building SimWorld's god layer.
- **Further along on exactly the systems SimWorld has not built yet.** Epoch has
  already shipped a knowledge-to-tech-point economy, an era ladder with charter gates,
  and a full marriage/birth/death/naming cycle — and has already measured where each
  one broke.
- **It has already paid for mistakes SimWorld is about to be able to make.** Epoch's
  tech table went from "15 rows, dry by Y4" to "78 rows, most of epochs 3–5 never
  reached in any measured run" — and SimWorld's research module ships 232 rows across
  8 eras today, an authored surface nearly three times Epoch's, with no measurement
  harness yet to say whether it is reachable at all.

Every claim about Epoch's code below cites `crates/epoch_core/src/sim.rs` or a
test/example file by line number, or a `docs/*.md` line for design intent. Where
something the docs describe has no code behind it, this file says so rather than
assuming.

## 1. The Knowledge economy

### How Epoch does it

Knowledge is a per-settlement resource that accrues **continuously**, unlike the book
pipeline it runs alongside (`sim.rs:8930-8947`). Three layers stack:

1. **Per-building rate.** A generator table (`KNOW_SOURCES`, `sim.rs:8986-9080`) lists
   which buildings teach the town something *this epoch*, at what base rate — six
   rows, one per epoch, each hand-tuned so practice (farms, hunts, smiths) pays early
   and institutions (scriptorium, school, academy) take over later without practice
   ever hitting zero. `knowledge_rate` (`sim.rs:9226-9248`) multiplies that base by:
   labour (`KNOW_LABOUR = [0.0, 1.0, 1.6, 2.0]` for 0-3 workers, `sim.rs:8953-8959`,
   capped at `KNOW_STAFF_CAP = 3`), refinement tier (`tier_out`), adjacency bonuses
   (wonder +0.25, school +0.15, civic +0.10, `sim.rs:9149-9199`), input availability
   (paper and books as smooth multipliers, never a hard gate — up to +0.35 and +0.45,
   `sim.rs:9130-9146`), and civilization doctrines (bound archives +25%, the Archive
   wonder +20%, `sim.rs:9202-9218`).
2. **Town aggregate with a soft cap.** `knowledge_raw_per_day` sums every building's
   rate (`sim.rs:9253-9255`); `knowledge_attenuate` then applies a **global scale of
   0.50** on the whole total, and above a **soft cap of 40.0 raw knowledge/day** bends
   the excess through `x^0.60` (`KNOWLEDGE_RATE_SCALE`, `KNOWLEDGE_SOFT_CAP`,
   `KNOWLEDGE_EXPONENT`, `sim.rs:9294-9312`). This is applied to the **town total**,
   not per building — a deliberate choice: per-building diminishing returns can be
   dodged by spreading across building types, and a town-total brake cannot be
   (`sim.rs:9264-9282`).
3. **The point curve — the "capped quadratic threshold".** `knowledge_needed(level)`
   (`sim.rs:9123-9126`) is `KNOW_CURVE_A * L^2 + KNOW_CURVE_B * L + KNOW_CURVE_C` with
   `A = 6.0`, `B = 26.0`, `C = 70.0` (`sim.rs:9106-9117`), **but only up to
   `KNOW_CURVE_CAP = 35`** (`sim.rs:9120`) — past that level the cost freezes at a
   flat `knowledge_needed(35) = 8,330` per point forever. `knowledge_tick`
   (`sim.rs:11027-11034`) adds the day's knowledge, then loops `while s.knowledge >=
   knowledge_needed(s.tech_level)`, converting the surplus into `tech_points` (a
   spendable currency) and incrementing `tech_level` (the difficulty ratchet the
   quadratic reads, not a copy of `techs.len()`).

The freeze constant is not original to Epoch — it is a measured fact about a
competitor, Settlement Survival, whose own curve is `30*L^2 + 30*L + 440` up to
`L=100` and then a flat `+5,936/level` to `L=130` (`docs/progression.md:274-279`).
Epoch's own comment calls this "the single most important finding" from that survey:
freezing the marginal cost is what lets late-game cadence *accelerate* instead of
sagging, because output keeps climbing while cost stops. Epoch's `KNOW_CURVE_CAP = 35`
sits at ~78% of its 78-row ladder, matching the reference's ~77% (`sim.rs:9118-9120`).

The labour reprice is the part of the system with the sharpest before/after
measurement: before it, the job-assignment scorer priced every teaching building at
the generic `1.0`, so a 25-year managed run ended with **14 Scribes and 14 Schools
standing at zero staff for the last eight years** (`sim.rs:9320-9330`).
`knowledge_job_score` (`sim.rs:9364-9387`) fixed this by pricing the *marginal* worker
against the age's best teacher, scaled by `KNOW_JOB_WEIGHT = 1.2` — deliberately below
a short food/water quota's 3.0-4.0 so research competes rather than dominating.

### What SimWorld should take

SimWorld's `ResearchManager` (`src/SimWorld.Core/Research/ResearchManager.cs`) is
currently a flat global rate: `ResearchPointsPerWorkTick = 0.00825f`
(`ResearchManager.cs:16`), applied uniformly regardless of which pawn or which
building performs the work, with no aggregate cap and no per-building table. That is a
faithful 1:1 port of RimWorld, where research literally is one number a pawn adds to.
Once buildings exist (`building` module, currently `planned`, `docs/status.json`),
Epoch's three-layer model is a strong template for the civilization-scale translation:

- **A per-`ThingDef` (or per-building-category) knowledge/research table**, keyed by
  era the way `KNOW_SOURCES` is keyed by epoch, fits the existing `EraDef`
  (`src/SimWorld.Core/Research/EraDef.cs`) — SimWorld already has the era concept,
  just nothing feeding a resource off of it yet.
- **The capped-quadratic-then-flat cost curve** is worth adopting **only if** SimWorld
  introduces a banked-point currency above the 232-project tree. RimWorld's own model
  spends progress continuously into `ResearchProjectDef.baseCost` with no banked
  points and nothing playing the role of `tech_level`, so the curve has nowhere to
  attach as things stand.
- **The town-aggregate soft cap, not per-building diminishing returns**
  (`sim.rs:9264-9282`) transfers directly to any civilization-scale rollup SimWorld
  builds: cap the total once, never once per building type, or a player games it by
  building variety instead of scale.
- **The labour-reprice lesson transfers to `WorkGiver`/`Pawn_WorkSettings` scoring**
  (`src/SimWorld.Core/Work/WorkGiver.cs`): a job type invisible to the priority scorer
  never gets staffed no matter how good its output table is. A future "Researcher"
  work type needs an explicit score against the age's best research building, the way
  `knowledge_job_score` prices against `knowledge_sources(epoch)`'s best row — not a
  flat priority.

## 2. Era ladder and charters

### How Epoch does it

`EraDef` (`sim.rs:2154-2210`) carries: an optional weather `pressure` and `min_pop`
floor, a research-mode override of both (`pressure_research`, `sim.rs:2157-2180`), an
era-open `grant` of starting stock, an `unlock`, a `buff`, and **three condition
lists**: `charter` (absolute bars, met once reached), `charter_research` (a full
replacement charter used only when `Settlement::research` is up — not additive
conditions, because Phase 3 needs to be able to *drop* a bar, not just add one,
`sim.rs:2190-2201`), and `charter_delta` (bars measured against a **baseline
snapshotted at era open**, `sim.rs:2203-2209`). The whole ladder is `ERAS_PER_EPOCH =
4` eras per epoch, authored today for 3 of `EPOCH_MAX = 5` epochs as `pub(crate)
static ERAS: [[EraDef; 4]; 3]` (`sim.rs:2280-2412`); epochs 3-5 fall through `era_for`
to a generated `[Pop, Mood]` charter (`docs/progression.md:13-15`).

`Charter` is an eleven-variant enum (`Pop`, `Stock`, `Mood`, `Books`, `Educated`,
`Coins`, `Building`, `Gen`, `Repelled`, `Refined`, `Logistics`, `sim.rs:2129-2142`),
each carrying its own target number (`charter_target`, `sim.rs:2215-2229`).
`apply_era` (`sim.rs:3525-3550`) fires on every era turn: it grants stock, unlocks,
and buffs, then **snapshots `era_base`** — one baseline value per `charter_delta`
condition — *after* the grant, deliberately, "an era that hands over 40 brick must not
thereby pay off its own 'store 20 more brick'" (`sim.rs:3542-3544`). `tick_era`
(`sim.rs:3552-3619`) checks all absolute conditions via `charter_met`, then all delta
conditions via `charter_delta_met` against the stored baseline — a **missing baseline
fails closed** (treated as unmet), which the code calls "the whole point": failing
open is the exact runaway this mechanism exists to prevent (`sim.rs:3557-3560`).

**Why delta charters exist at all — the monotone-charter trap.** A charter whose
conditions only ever increase completes the instant it is issued to a player already
past the bar. This was not hypothetical: it reproduced as the "epoch-5 runaway" — a
town already past `Pop(840)` completed a new era **every single day**, banking 4,700
legacy over 600 days against ~70 for a correct ladder, with 349 queued card draws and
a linear memory leak (`docs/progression.md:111-128`). `Charter::Refined(n)` has
exactly this monotone shape (`refined_count` only grows), so making it an era-turn
rule would reproduce the bug by construction, one rung at a time, for every era a
player has already cleared. The fix — delta semantics, measured from era open — kills
the trap at the root: "refine 10 more", never "reach 40%"
(`docs/progression.md:120-125`).

**"Advancement is never time-gated" (invariant #3) is not enforced by any type-level
guard** — nothing stops a future `EraDef` from reading `s.day` directly. It is
enforced by design discipline backed by measurement, and the project's own record
shows it slipping: `Charter::Pop(18)`/`Pop(26)` in epoch 0 were nominally predicates
over player state but were measured to be functionally a clock, because every
population lever a competent town has is already saturated (`food_security` steps at
`food > pop*12` against a measured 88x actual; the immigration window fires every
year) — leaving only `BIRTH_INTERVAL = 330` days and `FERTILE_LO = 900` days, both
constants, to gate the bar. The comment is explicit: "invariant #3 is violated in
substance while satisfied in form" (`sim.rs:2314-2332`). The fix authored four rules a
charter bar must obey to actually be player-actionable rather than
clock-wearing-a-predicate's-clothes (`sim.rs:2343-2386`): it must sit under the item's
own glut ceiling, must not name an equipment item consumed on possession, must name an
output no legal recipe-pin can zero out, and must be one the stand-in bot can also
clear (invariant #5, below) — each one learned by authoring a charter that broke it
and measuring the town starve or stall because of it.

**How an era turns, mechanically:** `tick_era` increments `era_idx`; at
`ERAS_PER_EPOCH` it resets to 0 and increments `epoch`, grants `legacy_base += 20`
plus a refinement bonus, and — because the raid scheduler's epoch check fires the same
day an epoch turns — gives a grace period (`next_raid.max(day + EPOCH_RAID_GRACE)`) so
the town is not sacked the instant it arrives (measured: every engaged archetype used
to turn an epoch and meet a warband the very next day, `sim.rs:3596-3608`). At the top
of the ladder (`epoch >= EPOCH_MAX`), the era retires to `None` rather than reissuing
— reissuing used to hand the settlement a charter it had already satisfied, completing
again every day forever (`sim.rs:3582-3593`, the same runaway class as above,
`docs/progression.md:1851-1856`).

### What SimWorld should take

SimWorld's `EraDef` (`src/SimWorld.Core/Research/EraDef.cs`) is currently a **pure
research-completion gate**: an era is `IsComplete` once every `ResearchProjectDef`
tagged with it is finished (`EraDef.cs:43-60`). That is a checklist over one axis of
play (research). Epoch's charter model reads population, stock levels, buildings
raised, mood, coins, raid outcomes, refinement, and road coverage — it measures **what
the player actually did in the world**, not just which tech nodes they picked.
Concretely:

- **Add a charter-style condition list to `EraDef`** alongside (not instead of)
  `Projects`/`IsComplete` — an era should also ask for population, stockpiled goods,
  or buildings raised, so an era turn reads as "your civilization has grown", not only
  "you finished a research queue".
- **The delta-vs-absolute split and the era-open baseline snapshot are the single most
  reusable piece of code here.** Any SimWorld condition that can only monotonically
  increase (buildings refined, roads paved, raids survived) needs this pattern or it
  walks into the same runaway Epoch already reproduced and fixed — a general
  mechanism, not Epoch-specific content.
- **The four rules for an actionable charter bar** (glut ceiling, no equipment-item
  bar, no pin-able-away output, bot-clearable) are a design checklist, not a mechanism
  to port: run every civilization-era condition through them before shipping it.
- **The epoch-turn grace period against the threat director** is directly relevant to
  `Storyteller`/`IncidentQueue` (`src/SimWorld.Core/Director/`): when eras start
  scaling threats on a turn (spec §10), hold the next incident back a beat, per
  Epoch's measured warband-the-next-day failure.

## 3. Tech: authored rows vs. reachability

### How Epoch does it

`Tech` is a 78-variant enum in age order (`sim.rs:11065-11151`); `ALL_TECHS` lists
them in table order (`sim.rs:11154-11239`). Each `TechDef` (`sim.rs:11268-11296`) can
do exactly one of four things: `unlock` a building early, `chain` open one rung of one
building's refinement ladder (66 of the 78 rows), `remould` open a per-instance
modification (5 rows), or `batch` sell the "upgrade everything at once" operation (1
row, deliberately never a second). Row cost is a flat, **epoch-indexed** price ladder,
`TECH_COST = [1, 2, 4, 6, 9, 13]` (`sim.rs:11266`) — a row's *age* is the axis that
escalates, chosen deliberately because the level curve's own escalation freezes at
`KNOW_CURVE_CAP`, so something else has to carry the "a later idea costs more" signal
or the back half of the table gets cheaper and cheaper in wall-clock terms
(`sim.rs:11241-11253`, `docs/progression.md:669-687`). Four authoring rules keep the
table internally consistent (`sim.rs:11341-11357`): a tier-2 chain rung never shares
an epoch with its tier-1 rung; a rung must be payable in materials the age that offers
it actually has (no epoch-0 town holds a unit of Clay, so no Clay-priced rung sits at
epoch 0); an `unlock` may never name a building the era ladder already grants for
free; and no new chain rung lands on a population-scaled building (Farm, Well, Hunt,
Smith, Kiln) that still teaches Knowledge, because refining it would multiply the
practice-era rate into a headcount stat.

**The reachability measurement (`examples/tech_panel.rs`, `tests/reach.rs`,
`tests/tech.rs`) is what the 78-row table replaced a 15-row placeholder over, and the
placeholder's own numbers were worse than assumed once measured rather than
eyeballed:**

| Metric (40 seeds, 45 years, before → after the epoch/tech rework) | Before | After (`game`, shipping flags) |
| --- | --- | --- |
| Seeds that go dry at all | 40/40 | 2/40 |
| Median first "dry" year (wallet full, nothing to buy) | Y4 | Y27 |
| Table bought out at all | 36/40, median Y13 | 0/40 |
| Rows bought (of the table's size) | 14.9 of **15** | 28.7 of **78** |

(`docs/progression.md:633-777`)

On the **engaged** arm (8 seeds, 25 years, an attentive `grow()` build order, not the
passive stand-in), the old 15-row table was bought out completely with 54 points left
unspent; the new 78-row table leaves **30.1 of 78 rows bought**, 25.1 points unspent
(`docs/progression.md:795-800`). The residual dry year-ends that remain are
concentrated in two seeds that sit at epoch 1 for 22 years holding 18,300 people — an
extreme population outlier, not a systemic gap (`docs/progression.md:807-810`).
**Epochs 3-5 are 37 of the 78 rows and 328 of the 434 total points, and no seed in any
measured run (40 seeds, 45 years) ever reaches epoch 3** — the back half of the table
is authored but "not dead content" only in the sense that a hand-run 40-year
game-shell session (not the panel) reached epoch 3 once
(`docs/progression.md:812-819`). The project's own conclusion: **"the next move on
this axis is the era ladder, not the row count"** — a shelf deep enough for a town
that stands in one age for 34 years would need to be six times epoch 1's, "which is a
ladder defect wearing a table's clothes" (`docs/progression.md:816-818`). In other
words: authoring more content cannot fix an unreachable epoch; only making the *epoch
turn faster or the charter more clearable* can.

### What SimWorld should take

SimWorld's `research` module ships **232 `ResearchProjectDef` rows across 8 `EraDef`s
today** (`docs/status.json`; confirmed by counting the shipped content: `grep -rc
"<ResearchProjectDef" src/SimWorld.Core/Data/Core/Defs/*/*.xml` = 232, 8 `<EraDef>`
entries) — an authored surface **three times** Epoch's 78, with **no reachability
panel of any kind yet.** This is precisely the situation Epoch's own history warns
about: authoring is easy to keep ahead of what a played (or bot-played) game can
actually spend, and the gap is invisible until someone measures it, at which point it
reads as a broken UI element (a "26 points to spend" banner glowing over an empty
shelf, `docs/progression.md:605-622`) rather than a content problem.

- **Build a reachability harness before authoring assumes 232 rows are all live.**
  Epoch's answer — a stand-in "bot"/autoplay policy plus a fixed seed panel, run to a
  year horizon, reporting rows bought vs. rows authored per era — is directly
  portable: a scripted pawn-AI stand-in run against `ResearchManager` over a fixed set
  of world/scenario seeds answers "how many of the 232 rows does a played civilization
  actually reach in N years" before that number becomes a live design argument instead
  of a measured one.
- **Price rows by the axis that is actually escalating.** If SimWorld ever gates
  research spend behind a banked-point currency (it does not today), Epoch's rule —
  escalate against the thing being bought, never a different axis of play — is the one
  number to get right first.
- **Treat "authoring is ahead of reachability" as the default hypothesis for any large
  content table**, not just tech: building, item, and incident tables will all
  eventually have the same shape, and Epoch's conclusion — fix the ladder/pacing, not
  the row count — generalizes.

## 4. Great Works

**Documented but no implementation found.** A repo-wide search for `GreatWork`/`Great
Work`/`Monumental` in `crates/epoch_core/src/sim.rs` and the test/example suites turns
up only two passing comments (`sim.rs:2792`, `sim.rs:2958`) that reference the *idea*
while describing unrelated code; there is no `GreatWork` type, no `Monumental`
threshold, and no category-accumulation counter anywhere in the source. This is Phase
4 of the progression rework and is explicitly listed as not landed
(`docs/progression.md:1-6`, `2112`).

### The spec, as documented

A Great Work is "not produced, it is earned" — the civilization's record of something
unusual: a first, or something achieved ahead of the curve. Doug's own example: the
first bridge raised before era 1.2 yields "engineering notes"
(`docs/progression.md:165-169`). The resolved design is an **accumulation, not a
trigger**: every action rolls into one of six proposed categories (Construction &
Engineering, Sustenance, Craft & Industry, Learning & Faith, Commerce, Arms — mapped
to existing building types, `docs/progression.md:207-217`); crossing a **Monumental
threshold** within a category is what yields the Great Work, turning "detect the
extraordinary" into "count and compare to a threshold"
(`docs/progression.md:193-199`). Monumentals are meant to front-load progression —
~85% of progress at early levels, falling proportionally as the Knowledge economy
comes online (`docs/progression.md:200-202`). A Great Work pays in three currencies at
once: Knowledge, a permanent civilization trait, and legacy/prestige
(`docs/progression.md:203-205`) — the last of which finally gives `legacy_base`
(`sim.rs`, written in four places today, `docs/progression.md:205`) something to be
*read* by, not just accumulated toward a HUD number.

**The machinery this would repurpose already exists**, just aimed at routine events
instead of extraordinary ones: `DeedKind` (`Founder`, `Mastery`, `Elder`, `Lineage`,
`sim.rs:1858-1862`) and `Deed` (`sim.rs:1865-1885`, which carries a citizen, a family,
a day, a job, and a numeric `bonus`) already express "a named person did a notable
thing, it carries a mechanical payload". What is documented as missing: a `DeedKind`
variant for the *extraordinary* rather than the routine; a conditionality clause
("first, and before X"); and a weight so a rare deed outranks a routine one
(`docs/progression.md:181-191`). The doc flags its own biggest open risk: `deeds` is
asserted bit-exact by the golden traces, so any new deed kind must be flag-gated or
fire only past where the goldens run (`docs/progression.md:189-191`) — the same
discipline invariant #1 imposes everywhere else in this codebase.

### What SimWorld should take

SimWorld has no equivalent system at all today — no per-category accumulation, no
threshold-crossing record, and (per the god-layer spec) no rollup that would read one.
This is a genuinely portable **design**, not code to port, because Epoch has none
written yet:

- **The reframe itself is the valuable part**: "detected by crossing a threshold in an
  accumulation counter" turns an open-ended "recognize something remarkable" problem
  into ordinary per-category tallying — the same trick for any "memorable moment"
  system a god game will want (a first settlement, a first war, a first famine
  survived).
- **Attach it to the Chronicle, not to Research.** SimWorld already has
  `Storyteller.Chronicle`/`ChronicleEntry`
  (`src/SimWorld.Core/Director/Storyteller.cs:8-66`) — a per-incident record with a
  tick, a def name, a target, and points. A category-accumulation and threshold system
  that emits a `ChronicleEntry` on crossing fits this narrator hook better than the
  research tree; "record of something out of the ordinary" belongs beside "every fired
  incident appends a narrator record" (spec §9), not inside `ResearchManager`.
- **Do not import "pays in three currencies" uncritically.** Epoch's Knowledge economy
  is the one thing standing between era turns; SimWorld has no directly analogous
  banked-point currency, so "a Great Work pays Knowledge" needs a native translation,
  not a literal port.

## 5. Lineage and families

### How Epoch does it

`Citizen` (`sim.rs:1906-1935`) carries `family`, `spouse`, `parents: [i64; 2]`,
`children`, `generation`, `last_birth`, and a randomly rolled `die_at`. `Family`
(`sim.rs:1898-1904`) is a household: `surname` (display-only, an index into a name
table), `living`, `total`, `gens`, and `prestige`. The full cycle, in `tick_day`:

- **Coming of age** at `ADULT_DAYS = 900` (`sim.rs:1820`); a citizen who banked
  `schooling >= SCHOOL_DAYS = 300` becomes `educated` (`sim.rs:8129-8140`).
- **Marriage** pairs unmarried adults under `FERTILE_HI = 4950` days, scanning in
  citizen-index order and skipping same-family pairs as an incest guard
  (`sim.rs:8143-8158`). **A wedding founds a new house** — this is a fix, not the
  original behavior. The reference bench merges instead: the lower-prestige spouse
  joins the higher-prestige family, prestige is added by deeds, and prestige decides
  the next merge — a Galton-Watson process with a thumb on the scale. Measured at year
  20 under the merge rule: **one lineage held 427 of 472 living citizens (90%),
  climbing to 91% by year 25**, across a nominally healthy 14-19 families
  (`sim.rs:8162-8187`, `tests/lineage.rs:1-13`). The fix — flag-gated behind
  `research` because it reorders marriage pairing and therefore the birth-roll RNG
  stream — makes every wedding start a fresh `Family` inheriting the couple's own
  `generation` (`sim.rs:8194-8212`).
- **Births** roll once per eligible couple per day (only the lower-id spouse's
  iteration fires, to avoid a double roll, `sim.rs:8272-8275`), gated by age in
  `[FERTILE_LO=900, FERTILE_HI=4950]` days and a `BIRTH_INTERVAL = 330`-day cooldown
  since the parent's last birth (`sim.rs:1823-1825`, `8263-8271`). Probability is
  `0.25 * (0.5 + mood/100) * food_security * midwifery`, where `food_security` is
  `1.0` if the settlement clears a food-days threshold else `0.4`, and a "midwives"
  doctrine multiplies by `1.2` (`sim.rs:8284-8290`). A new citizen's `generation` is
  one past its parent's (`sim.rs:3285-3289`); a parent's fifth child raises a
  `DeedKind::Lineage` deed (`sim.rs:8302-8304`).
- **Death** rolls `die_at` once, at creation, as `born + DEATH_LO + rand()*(DEATH_HI -
  DEATH_LO)` with `DEATH_LO = 4950`, `DEATH_HI = 8100` days (`sim.rs:1821-1822`,
  `3283-3284`) — i.e. every citizen's lifespan is pre-determined at birth, not
  re-rolled per tick. `kill` (`sim.rs:3344-3393`) marks the citizen dead, removes them
  from their house and workplace, sets the surviving spouse's and any parent/child's
  `grief` timestamp, and records a `DeathCause` (`Age`, `Starved`, `Thirst`, `Plague`,
  `Banished`) on the `SimEvent::Death` log entry (`sim.rs:3375-3388`) — the cause
  travels as a four-variant enum, never a string, all the way to the render layer. An
  elder who dies of age past day 7,900 raises a `DeedKind::Elder` deed
  (`sim.rs:3392-3393`).
- **Naming**: two RNG draws are captured per citizen at creation (`name_seed: [f64;
  2]`, `sim.rs:1934-1935`, `3280-3283`) so the render layer can compose the same name
  deterministically without touching the shared RNG stream — the core never holds or
  emits the string itself.
- **Immigration** (every 360 days, when food-secure, there is room, and mood exceeds
  50) spawns a **new** family outright with two adults already married to each other
  (`sim.rs:8317-8339`) — the only path, besides the marriage fix, that creates a
  family from nothing.

### What SimWorld should take

`docs/status.json`'s own `pawngen.lineage` item says "birth and lineage as the main
source of citizens is still open," and `social.romance` ("Romance/rivalry thresholds;
marriage and breakup") is listed `done: false` — this is close to a direct request for
exactly what Epoch has already built and measured:

- **Found a new household at marriage; never merge into the higher-prestige family.**
  The single most concrete, load-bearing lesson in this document. Epoch measured the
  merge rule producing 90%+ lineage concentration inside 20-25 years on a healthy
  population — a bug that would read to a player as "why does every citizen share
  three surnames by generation four" — and it is cheap to avoid from day one, since
  fixing it later (as Epoch found) reorders every downstream RNG draw that depends on
  marriage pairing.
- **A pre-rolled `die_at` at birth**, rather than a per-tick mortality check, is
  cheaper (no per-tick roll over the whole population), trivially save-compatible, and
  makes "how long will this pawn live" answerable in advance for UI. Maps directly
  onto `Pawn_AgeTracker` (`src/SimWorld.Core/Pawns/Pawn_AgeTracker.cs`), which tracks
  life stages today but has no death-at-age concept.
- **A small `DeathCause` enum on the death event, never a string**, fits SimWorld's
  existing "not string-free, but defNames not prose" convention (see §6) — feeding
  `Storyteller.ChronicleEntry` is a natural extension of the existing Chronicle, not a
  new subsystem.
- **The birth-probability formula's shape (base rate x mood x food-security x doctrine
  multiplier) is a reasonable template**, but the literal numbers are Epoch-specific
  tuning for its own compressed lifespan (adults at 2.5 years, dead by 14-22 — an
  intentional, parked "age fiction" oddity, `docs/progression.md:146-147`) and should
  not be copied; SimWorld's pawns already run on RimWorld's real age/fertility clock
  and births should be built against that, not Epoch's compressed one.
- **Name-seed capture without emitting the string from the core** is exactly the
  pattern SimWorld's own `PawnBioAndNameGenerator.cs` already follows — good
  confirmation the two projects converged on the same discipline independently.

## 6. The Historian

`docs/historian.md` is already read in full and is not re-summarized here.
**Confirmed: none of it is implemented.** A search for `Historian` across
`crates/epoch_core/src/*.rs`, `crates/epoch_core/tests/*.rs`, and
`crates/epoch_core/examples/*.rs` returns zero matches; `docs/historian.md` itself
opens with "Status: draft spec, not implemented" and closes with a five-step build
order, none of which has landed (`docs/historian.md:3-4`, `127-134`).

**What the deed-log substrate looks like, because it already exists and is exactly
what a Historian would query:**

- `SimEvent` (`sim.rs:2982-3018`) is a fourteen-variant, string-free enum — `Birth`,
  `Death{family, cause}`, `Wedding`, `Immigrants`, `EraComplete`, `EpochTurn`,
  `BookWritten`, `RaidIncoming`/`RaidRepelled`/`RaidLoss`, `Collapse`, `Festival`,
  `Raised{key}` — appended to `s.events: Vec<(day, SimEvent)>` on every occurrence,
  explicitly documented as "data only, no strings; the render layer writes the prose.
  Pure appends: zero effect on the RNG stream or goldens" (`sim.rs:2979-2981`).
- `Deed` (`sim.rs:1865-1885`) and `DeedKind` (`Founder`, `Mastery`, `Elder`,
  `Lineage`, `sim.rs:1858-1862`) are the higher-level "a named person did a notable
  thing" record, carrying `citizen`, `day`, `family`, an optional `job`, and a numeric
  `bonus` — already exactly the shape historian.md's tier-1 "retrieval + templates"
  plan would query.
- `BookRec` (`sim.rs:1889-1893`) pairs a `DeedKind` with a job and bonus as the record
  of a book written about a deed.

This substrate is real and load-bearing (asserted bit-exact by the golden traces,
`docs/progression.md:189-191`), but everything historian.md actually proposes — the
salience scoring function, seeded personality trait vectors, the bounded query
grammar, epoch-voiced templates, and the oral-history fidelity-decay mechanic tied to
the record-keeping economy — has no code behind it anywhere in the crate.

### What SimWorld should take

SimWorld's `Storyteller.Chronicle` (`src/SimWorld.Core/Director/Storyteller.cs:8-66`)
is the direct structural analog of Epoch's `s.events` log, and is further along in one
respect: `ChronicleEntry` already carries free-form text (`targetLabel`, a raw string,
`Storyteller.cs:19`), where Epoch's `SimEvent` carries none at all. That difference
matters for what to adopt:

- **historian.md's three-tier build order (retrieval+templates, then scored
  salience/traits, then an optional local LM for phrasing only, facts injected) is a
  clean, low-risk shape for whatever narrator SimWorld's open "director persona name"
  decision** (`docs/status.json` decisions, id `director`) becomes. Tier 1 needs
  nothing SimWorld doesn't already have: `ChronicleEntry` is already a queryable,
  append-only log.
- **The hard constraints are general safety rules, not Epoch-specific**: narration
  lives in the presentation layer, never the core (already SimWorld's own rule — spec
  §11: "the host reads simulation state and never mutates it"); no inference on the
  sim tick; retrieval grounded in the log, never generative about facts a player could
  check.
- **The advisor-policy-as-measurement-instrument idea** (a deterministic, versioned
  weight vector standing in for "how a bot would play", used to generate reachability
  panels) is the same mechanism §3 recommends for tech reachability — the two findings
  independently arrive at the same tool.

## 7. The invariants and the method

Epoch names five invariants explicitly (`docs/progression.md:1832-1842`,
`docs/competitors.md:441`) and enforces them with a specific measurement harness. Each
is stated below with what it means for SimWorld, which does **not** share all of
Epoch's constraints.

| # | Epoch's invariant | Applies to SimWorld? |
| --- | --- | --- |
| 1 | Goldens are bit-exact, f64 included — compared via `.to_bits()`, not `assert_eq!` on the float (`golden.rs:60-73`, `sim_golden.rs:1-6`) | **Yes, directly.** SimWorld's own ground rules already require determinism through seeded `RandomStream`s and Scribe round-trips (`CLAUDE.md`); a bit-exact golden-master harness against a reference trace (year-checkpoint or tick-checkpoint) is a stronger, adoptable version of the same discipline, not yet built for SimWorld. |
| 2 | The core is string-free: no names or prose in `epoch_core` (`docs/progression.md:1840`) | **Does not apply as stated, and should not be imported blindly.** SimWorld's core is explicitly *not* string-free — it is built entirely around `defName` string identifiers (`CLAUDE.md`: "Def references are bare `defName` strings"), and `ChronicleEntry` already stores a raw `targetLabel` string (`Storyteller.cs:19`). The *spirit* transfers (mechanical state should be data an enum/def reference can express, not narrator prose baked into the sim), but "no strings at all" is the wrong bar for a defName-based architecture. |
| 3 | Advancement is never time-gated — a predicate over player state, never a clock | **Yes, and SimWorld should adopt the enforcement method, not just the rule.** Epoch's own history shows this invariant is easy to violate *in substance while satisfied in form* (§2 above); the fix was a checklist of four concrete authoring rules plus a measurement panel, not a mechanism. SimWorld's god-layer edicts and era gates should get the same checklist treatment before they ship, especially since SimWorld's spec already commits to "eras... carries a civilization... gates content" (spec §10) without yet specifying how a gate is checked. |
| 4 | Rendering reads, never writes | **Yes, already a stated SimWorld rule.** Spec §2/§11: "Layer 6 is one-way: the host reads simulation state and never mutates it." Identical principle, already adopted independently. |
| 5 | Tune the stand-in player, not the simulation — "if the advisor bot's town dies of thirst, the bot should have built more wells; deaths are the game working" (`docs/competitors.md:441`) | **Yes as a balance principle, but adopt its caveat too.** Epoch's own competitive analysis flags that this principle is correct about balance and silent about legibility: Banished shipped the identical principle and has spent a decade fielding "I had 2,000 food and an elder starved and I have no idea why" (`docs/competitors.md:441-443`). SimWorld should pair this invariant with a cause-of-death/cause-of-failure record from day one (§5 above already gives SimWorld a `DeathCause`-shaped hook), not add it retroactively. |

**The measurement harness — the most transferable piece of engineering here.** Three
layers, each independently portable:

1. **Bit-exact goldens against a reference trace**, compared via `f64::to_bits()`
   (`golden.rs:60-73`), with primitives (hash, RNG stream, noise samples) held to zero
   tolerance and only transcendental-heavy worldgen summaries given a documented ULP
   budget because `sin`/`cos`/`powf` rounding is implementation-defined across
   languages (`golden.rs:1-11`). `sim_golden.rs` extends this to year-checkpoint
   traces of a full simulation run — RNG call count, population, education, wealth,
   stock levels, all compared exactly, with the RNG call count singled out as "the
   bisector — if a year fails, the first differing rng count pins the day the streams
   split" (`sim_golden.rs:1-6`).
2. **A default-off flag plus its own seeded RNG stream** (the `war_rand` pattern,
   `docs/progression.md:1836-1839`) lets a new system run in parallel with the old one
   with zero risk of re-baselining the goldens — every one of Epoch's
   Knowledge/tech/charter systems above ships behind `Settlement::research`, checked
   to be a no-op with the flag down.
3. **A fixed, named seed panel driven by scripted archetypes, not a single playtest.**
   `examples/demo_arc.rs` runs a 24-seed panel (40 for some probes) against nine
   reactive archetypes (`idle`, `roofs`, `reader`, `dawdler`, `chartonly`, `badpin`,
   `grower`, `industry`, `spender`, `demo_arc.rs:202-267`) plus the autoplay bot, each
   a "reactive policy on a click cadence... the shape a person has: they look at the
   screen every so often and place one thing because of something they saw"
   (`demo_arc.rs:9-13`). The panel is configured through environment-variable
   ablations (`EPOCH_PIN=0`, `EPOCH_ASSIGN=0`, `EPOCH_REACH=0`, etc.,
   `demo_arc.rs:44-58`) so a single run can isolate exactly which shipped mechanic a
   given number depends on, and the panel's own header records a history of numbers
   silently going stale when a probe drifted from what `epoch_game::main` actually
   ships (`demo_arc.rs:17-38`) — a documented, recurring failure mode of measurement
   code itself, not just the thing being measured.

**SimWorld does not have any of this yet** — `docs/status.json` records 487 xUnit
tests, but nothing resembling a fixed-seed archetype panel, a golden bit-exact trace
against a reference, or an env-var-driven ablation harness. Given that SimWorld's
stated population target is "as large as we can" at RimWorld depth and its tech tree
is already 3x Epoch's authored surface before anyone has measured what a played game
reaches, this is the single highest-leverage piece of infrastructure to build early
rather than after the first "why does this number feel off" argument.

**The recurring lesson: check the reference before tuning.** Epoch's tool-ratchet
deadlock (`Quarry` needs a `Tool` to cut `Stone`, `Smith` needs `Stone` to forge a
`Tool` — once both stores empty, the graph has no entry) looked like a bot AI problem
and two bot-side fixes were tried and measured to fail (raising the quarry-building
floor changed which seeds stalled rather than fixing the stall; raising spare-dwelling
counts moved unrelated numbers, `docs/economy.md:1372-1395`). **The actual fix was one
missing content row**: the reference game (Settlement Survival) ships a toolless
`Quarry` recipe alongside a tooled `Deep Quarry`, and Epoch had only ever authored the
tooled one — "the answer was in the config... one item we never authored"
(`docs/economy.md:1397-1410`). Adding the missing toolless rung fixed five of eight
stalled seeds outright, faster than the pre-ladder bot on five of eight
(`docs/economy.md:1412-1417`). The general rule this earns: **when a system reads as
an intractable balance problem, check whether the reference content this system is
modeled on is fully authored before spending effort tuning the policy or the curve
around it.** This applies directly to SimWorld's 1:1-port discipline — a RimWorld
mechanic that reads as broken in the C# port is more likely to be an under-authored
Def (a missing recipe, a missing trait, a missing hediff stage) than a bug in the port
itself, and `CLAUDE.md`'s own rule ("port RimWorld 1:1 first") already points at
checking the source before touching the mechanism — this finding is the concrete
failure mode that rule exists to prevent.

## Adopt / adapt / reject

| Idea | Verdict | Reasoning |
| --- | --- | --- |
| Bit-exact golden-master traces (`.to_bits()` comparison, year/tick checkpoints) | **Adopt** | Directly strengthens SimWorld's existing determinism rule; no architectural conflict. |
| Default-off flag + parallel seeded RNG stream for new systems (`war_rand` pattern) | **Adopt** | Cheap, general, and solves a real problem SimWorld will hit the moment two systems need independent randomness without breaking existing tests. |
| Fixed-seed archetype panel + env-var ablations, as a reachability/balance harness | **Adopt** | The highest-leverage gap SimWorld has today given a 232-row tech tree with zero measurement of what a played game reaches. |
| Per-building generator table for a research/knowledge resource | **Adapt** | Right shape once SimWorld's `building` module lands; needs era-keying via SimWorld's existing `EraDef`, not a literal port of Epoch's building keys. |
| Capped-quadratic-then-flat cost curve for a banked point currency | **Adapt, conditionally** | Only relevant if SimWorld introduces a point-spend layer above `ResearchProjectDef`; RimWorld's continuous-progress model has no place for it as-is. |
| Town/civilization-aggregate soft cap over per-building diminishing returns | **Adopt** | General anti-gaming principle for any rollup SimWorld aggregates from many producers; cheap to build correctly the first time. |
| Delta-charter semantics with an era-open baseline snapshot | **Adopt** | Directly prevents a reproduced, well-documented runaway bug (the monotone-charter trap); a general mechanism, not settlement-game-specific. |
| Four rules for an "actionable" charter/era-gate condition | **Adopt as a checklist**, not code | Design discipline, applied by the author of each new era condition, the same way SimWorld already treats "assert behaviour over literals" as a review rule. |
| Charter conditions reading world/population/building state, layered onto era completion | **Adapt** | SimWorld's `EraDef.IsComplete` is pure research-checklist today; adding Epoch-style state conditions makes an era turn feel like civilization growth, not a tech queue draining. |
| Great Works: category-accumulation crossing a Monumental threshold | **Adopt the design, since there is no code to adopt** | Genuinely unbuilt in Epoch too; the reframe (accumulation, not detection) is sound and has no SimWorld equivalent yet. Attach to `Storyteller.Chronicle`, not `ResearchManager`. |
| Marriage founds a new household; never merges into the higher-prestige family | **Adopt outright** | The single most concrete, already-measured lesson in this document (90%+ lineage concentration under the merge rule); cheap to get right from the start of SimWorld's families work. |
| Pre-rolled `die_at` at citizen creation, not a per-tick mortality check | **Adopt** | Cheaper, deterministic, save-friendly; fits `Pawn_AgeTracker` naturally. |
| Epoch's literal birth/fertility/lifespan-in-days constants | **Reject** | Tuned for Epoch's own deliberately compressed lifespan (adults at 2.5 years); SimWorld's pawns already run on RimWorld's real age curves and should build births against that clock. |
| `DeathCause`-style enum traveling with the death event, never a string | **Adopt** | Compatible with SimWorld's defName-based (not string-free) convention; a natural, small extension to `Storyteller.ChronicleEntry`. |
| "Deaths are the game working" as the whole design answer to failure | **Adapt** | Adopt as the balance principle, but pair it with a cause-of-death/failure record from day one — Epoch's own competitive research flags this exact gap (the Banished precedent) rather than treating it as settled. |
| Historian: retrieval-grounded, tiered build order (templates -> salience/traits -> optional LM) | **Adopt the shape**, defer the build | No code exists to adopt; the build order and hard constraints (no inference on the sim path, narration never writes) are sound general rules SimWorld's own narrator decision should follow whenever it is made. |
| Historian: fidelity decay tied to a record-keeping economy (oral history degrades before writing exists) | **Adapt** | A strong flavor idea, but it is coupled to Epoch's specific Knowledge/Paper/Book economy; SimWorld would need its own record-keeping resource before this mechanic has anything to hook into. |
| "Core is string-free" as a literal rule | **Reject for SimWorld as stated** | SimWorld's core is built on `defName` strings by design (Def system, `CLAUDE.md`); the applicable version of this rule is "no player-facing prose in the core," which SimWorld already mostly follows via defNames and enums, not "zero strings." |
| Settlement-aggregate rollups as the whole simulation unit (one settlement = one set of numbers) | **Reject as a model for SimWorld's per-pawn depth** | Epoch is explicitly a settlement-aggregate game; SimWorld's stated pillar is RimWorld-depth *per agent* at civilization scale. Epoch's per-building/per-settlement math is a good source of *techniques* (soft caps, labour curves) but its object model — citizens as lightweight structs inside one `Settlement`, not full `Pawn`s with health/needs/mood/skills — is the wrong target architecture to imitate directly. |
| Free-form point allocation with no prerequisite web ("Palworld-style" tech spend) | **Reject** | SimWorld's decided direction is "large authored tech tree of societal evolution" with `docs/status.json`'s own tech decision noting the data model "must not preclude" procedural extension later — a prerequisite-free, freely-allocated point spend is a different design bet than the DAG SimWorld has already built and shipped. |
