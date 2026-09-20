# Goal renewal: why a run should not go quiet

The single most consistent finding from researching this genre, and the design
that follows from it. This is a decision record, not a plan — the mechanism it
describes is not built yet, and the reasoning matters more than the shape.

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
