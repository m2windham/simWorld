# Goal renewal: why a run should not go quiet

The single most consistent finding from researching this genre, and the design
that follows from it. This is a decision record, not a plan — the mechanism it
describes is not built yet, and the reasoning matters more than the shape.

> ## ⚠ Status: §8 step 1 ran, and it falsified the first tension
>
> Relative standing — the candidate this document proposed building on — is
> **mathematically constant** after a run's opening years. Measured over 240
> in-game years, its mean year-on-year movement by third was **0.1202, then
> 0.0000, then 0.0000**: flat for 160 consecutive years, not merely quieter.
>
> That is §2's objection to `MomentCurator` reached by a different route. Not a
> depleting novelty budget — a quantity that cannot change. §7 named exactly this
> test and it came back negative, so **§8 step 2 does not proceed**. The full
> result and what it implies are in [§9](#9-what-the-measurement-actually-found),
> which was written after the sections above and supersedes them where they
> disagree.
>
> The sections above are left as written rather than quietly edited, because the
> reasoning that led to a wrong prediction is the useful part.

## 1. The failure mode, which is nearly universal

"Ran out of things to do" is the named, recurring complaint in **every** game
surveyed: Banished, Cities: Skylines once traffic is solved, Factorio after the
rocket, RimWorld's late game, Dwarf Fortress forts abandoned rather than lost —
and Nova Roma, which showed it within six months of Early Access rather than
after years. It is not a maturity problem that we can outrun.

In no case was the fix more content. Factorio's actual answer was to bolt a
second game on top, because the core loop could not renew itself. The one
mechanism in the survey that never fully runs out is the one that **does not
pre-author goals at all** — it reads live state and selects from it.

## 2. The correction: `MomentCurator` cannot be it

An earlier note in this project — mine — said `MomentCurator` is structurally a
storyteller pointed backwards, and that turning it forwards would give us the
renewal layer. **Reading it properly shows that is wrong, and wrong in an
instructive way.**

`MomentCurator` is excellent at what it does, and its design is deliberate: a
moment is a **first of its kind**, an **era crossing**, or a **broken record**.
Its own class doc explains that this makes "a routine century does not produce
hundreds of moments" true *by construction* rather than by tuning a threshold.

Look at what each of those three rules does over time:

| Rule | Behaviour as a run continues |
| --- | --- |
| First of its kind | Fires **once per category, ever**. Strictly depleting. |
| Era crossing | Bounded by the era ladder. A fixed, finite budget. |
| Broken record | Each new record must beat every previous one, so it fires **less and less often** by definition. |

All three are bounded, and two of them decay. That is exactly right for memory —
you want the hundredth raid to be unremarkable. But it means that pointed
forwards, `MomentCurator` would **go quieter the longer you played**, which is a
precise mathematical description of the failure mode in §1.

> **Novelty is a depleting resource. A renewal layer cannot be built on one.**

This is worth stating plainly because the mistake was attractive: the component
was there, it already read state, and it already scored significance. It was the
obvious answer and it was the wrong one.

## 3. What it must be built on instead: tensions, not firsts

A source of occasions that does not deplete has to read something that is
**always present to some degree and always changing**. Not "has this happened
before" but "how much is this true right now".

Candidates, all of which we already simulate:

- **Relative standing.** A neighbour's strength against ours. Never resolved,
  always moving, and it cuts both ways — their weakness is an opportunity, their
  strength is a threat.
- **Surplus against capacity.** A granary fuller than it has ever needed to be is
  an invitation. So is one that is emptying.
- **The gap between who we say we are and what we do.** We now have an ideology
  with precepts that actually affect mood. A civilization whose behaviour drifts
  from its own stated beliefs is a standing tension with no natural end.
- **Unfinished business.** The chronicle remembers who wronged us. If they still
  exist, that is a live tension, and it is *already recorded*.
- **Demographic shape.** Too many young, too few elders, a generation with no
  role — these oscillate rather than resolve.

None of these can be exhausted, because none of them is an event. They are
readings.

## 4. The shape: a Prospect

A **Prospect** is an *offered occasion*: something this civilization could do,
surfaced because live state makes it apt right now.

It is deliberately **not** a quest with authored content. It is a pointer at
machinery that already exists:

- *"Hold a gathering."* The ritual system is built, tested, and — as the ideology
  lane proved by ticking a real game for 6,000 ticks — **nothing in the
  simulation ever decides to.**
- *"Seat an elder."* Same: `Ideo.TryAssignRole` works and has no caller with an
  intention.
- *"They are weaker than they have been in a generation, and you remember what
  they did."* Diplomacy exists; the chronicle holds the grievance.
- *"The granary has never been this full."* A great work becomes possible.

The player takes it or ignores it. **Ignoring costs nothing directly** — but the
state that produced it persists, so it will be offered again, differently, and
the tension keeps its weight.

## 5. The rules this layer is held to

**The lever test applies unchanged.** A prospect may only offer what the
simulation can carry to its consequence through its own machinery. If taking it
would require writing a value into state, it is not a prospect. Every example in
§4 is a caller for something already built — which is the point, and also why
this layer is cheap relative to what it unlocks.

**No authored scenario lists.** The authored part is the *template* and its
reading of state. The moment we are maintaining a list of situations, we have
rebuilt the content treadmill that §1 says does not work. If a small number of
templates cannot produce enough variety, the templates are reading state too
thinly — that is the thing to fix.

**Under-claim, never over-claim.** A prospect that says the neighbour is weak
must be right about that at the moment it is offered, and must withdraw when it
stops being true. An offer the world no longer supports is the letter-choice
defect one level up: the player acts on a belief we handed them and it was
stale.

**A bad decision must be allowed to be bad.** A prospect is an observation, not
advice. Taking a ruinous one is the player's call.

**Determinism.** Selection goes through a seeded `RandomStream`, like everything
else. The same world and the same seed offer the same prospects.

## 6. Why this is the highest-value remaining work

It is the missing third leg.

| Leg | State |
| --- | --- |
| The world can act on the player | Done — phase 2, eight defect types, all measured |
| The player can act on the world | Done — orders, standing rules, diplomacy, research, roles, trade |
| **Something gives the player a reason to** | **Nothing** |

It also retires a class of defect rather than one instance. This project keeps
finding machinery that is built, tested, and never called — and the ideology lane
surfaced the subtlest flavour yet: **built, callable, and nothing ever decides
to.** Rituals and roles are not a separate gap waiting on their own lane. They
are consumers waiting on this one.

## 7. What would falsify this

Worth writing down before building, so the answer is not decided by whoever is
most invested:

- **Run a long game with prospects switched off and on, same seed.** If the
  chronicle reads the same, the layer is decoration.
- **Count prospects offered per century, late versus early.** If the rate decays,
  it is `MomentCurator` again wearing a new name, and §2 applies.
- **Check the player ignored some.** If every prospect is taken, they are not
  choices, they are a to-do list — which is the Factorio endgame complaint in a
  different costume.
- **Ask whether a run is worth recounting.** The bench already reports curated
  moments, days with anything happening, and mood and food swings. Flat is the
  failure state. A renewal layer that does not move those numbers has not worked,
  and moving them is necessary rather than sufficient.

## 8. Build order

1. **One tension, read honestly, with no offer attached.** Instrument it and
   check it actually moves over a long run before building anything on top.
2. **One prospect template** over that tension, pointing at machinery that
   already exists — the ritual is the natural first, because it is the clearest
   case of "nothing ever decides to".
3. **The seam.** Prospects reach the host as values and `defName` handles, and
   are taken through a command, exactly like everything else.
4. **More tensions, only once the first proves it does not decay.**

Step 1 is deliberately unglamorous and deliberately first. The whole argument in
§2 is that a generator can look right and still go quiet, and the only way to
know is to measure the rate over time rather than to reason about it.

---

## 9. What the measurement actually found

§8 step 1 ran. `Director/StandingReader.cs` reads relative standing; the bench's
`tension` suite samples it yearly. Run: 240 in-game years, 241 samples, 864
million ticks, seed 12345.

The four questions from §8, answered with numbers.

### 1. Does it move? Barely, and only while the player was dying

| | |
| :--- | ---: |
| Headline gap, min → max | 3.289 → 8.943 |
| Standard deviation | 0.418 |
| Of which, years 0–3 | the **entire** range |
| Years 3 → 240 | 3.29 → 3.30 |

A range of about **0.01 across 237 years** — while own strength went from 901 to
34,919,370, a factor of 38,756. The subject grew forty thousand-fold and the
reading did not move.

### 2. Does its rate of change decay? Yes, to exactly zero

Mean absolute year-on-year movement, log2 units, by third of the run:

| Series | 1st third | 2nd third | 3rd third |
| :--- | ---: | ---: | ---: |
| Gap (headline) | 0.1202 | **0.0000** | **0.0000** |
| Gap to weakest rival | 0.1202 | **0.0000** | **0.0000** |
| Gap to strongest rival | 0.0011 | **0.0000** | **0.0000** |

This is the load-bearing answer and it is unambiguous. Not "quieter" — flat.

### 3. Does it survive the world settling? The world never settled

Rivals stayed at 13–14 throughout; zero samples were undefined. The reading
remained perfectly available and perfectly useless. **The failure mode is
meaninglessness while still being defined**, which is worse than unavailability:
an offer built on it would keep firing forever with nothing behind it.

### 4. Is it deterministic? Yes

Same seed, same series, both subjects. The reader draws no randomness at all.

## 10. Why it is constant, and where the fix is

Two structural causes, both verified in the code rather than inferred:

1. **Growth is noiseless and identical everywhere.** `Settlement.cs`'s
   statistical path is `statisticalPopulation * (1.0 + StatisticalNetGrowthPerYear)`
   with `StatisticalNetGrowthPerYear = 0.045f` — a compound multiplier with no
   random draw. Every Statistical cohort grows at exactly the same rate, so
   **ratios between civilizations are constant by arithmetic.** No reading of
   relative size can vary while this holds.
2. **Emergence permanently switches itself off.** `EmergenceManager`'s
   `if (existingCount >= def.maxCountAtGameStart) continue;` skips any faction def
   already at its cap, and a crowded world generation saturates every def at
   startup. The civilization count sat at 15 for 240 years and could never move.

**The fix is in the simulation, not in the reading.** A prospect layer over a
constant is a prospect layer over a constant however well it is written.

## 11. What this does and does not prove

**It does not prove tensions in general are the wrong idea.** One tension was
tested. Over-claiming from a single negative would be the same error in the
opposite direction, and this project has a rule about that.

**What it does establish:** "always present" does not imply "always changing",
and §3 quietly assumed it did. Every candidate tension in that list now needs
its movement *measured* before anything is built on it — not argued for from the
fact that it is continuous. That is a cheap check and it has just caught a design
that looked obviously right.

## 12. A larger finding that arrived sideways

The lane could not measure the player's own civilization, because **it dies
within five years in every configuration tested**, unwatched, at founding scale:

| Configuration | Own population by year | Deaths |
| :--- | :--- | ---: |
| `--solo false --band 25` | 25, 15, 2, 0 | 24 injury |
| `--solo true --band 25` | 25, 10, 5, 1, 0 | 25 injury |
| `--solo true --band 40` | 40, 27, 23, 9, 2, 0 | 46 injury |
| `--solo false --band 40 --seed 777` | 40, 17, 14, 3, 2, 0 | 38 injury |

The `--solo true` runs have **no rival civilization in existence at all**, so this
is not conquest. It is `SettlementRaidResolver` settling threats against an
unattended settlement. And there is no larger-band workaround: spec §5b.3 fixes
`FoundingBandRange` at 20–40, so 40 is the whole budget.

This contradicts `docs/design/phase-2-pressure.md` head-on, which states:

> A settlement that is well built must be genuinely fine unwatched.

It is not fine. It is dead in five years with nothing attacking it but the
abstract threat resolver. **This is more urgent than goal renewal**, because a
renewal layer offering occasions to a civilization that cannot survive its own
first decade is furniture. It was found only because measuring one thing honestly
required a subject that lasts, and there wasn't one.
