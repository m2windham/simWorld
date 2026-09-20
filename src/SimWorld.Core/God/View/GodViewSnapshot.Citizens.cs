using System;
using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.God.View
{
    /// <summary>
    /// The drill-down: from the civilization aggregate, to a settlement's people, to one named person's state
    /// and history. <c>docs/design/player-first.md</c> §7 makes this a hard requirement —
    /// <i>"there must be an unbroken path from the civilization aggregate down to one named person's actual
    /// state and history"</i> — on the strength of the nearest published failure to where this project sits:
    /// Frostpunk 2 moved from named citizens to faction statistics and was reviewed on exactly what it lost.
    ///
    /// <para/><b>This does not widen the rollup, and that distinction is the whole design.</b>
    /// <see cref="GodViewSnapshot"/>'s own doc refuses per-citizen detail because "opening every person to
    /// paint a civilization is exactly what §11.3's tiering exists to prevent — a named citizen is a
    /// different, narrower query". These are that narrower query. Nothing here is folded into
    /// <see cref="Capture()"/>; <see cref="CitizensOf"/> is asked for one settlement the host has already
    /// chosen, and <see cref="Citizen"/> for one person inside it.
    ///
    /// <para/><b>Static, and taken at the tick they are called on.</b> They are not slices of any snapshot the
    /// host is holding — a host may open a citizen several frames after the snapshot beside it was taken, and
    /// pretending otherwise would be the "a live reference tears" failure <see cref="GodViewSnapshot"/> was
    /// built to avoid, one level down. Each result carries its own <c>TicksGame</c> so the host can tell
    /// whether the two agree.
    ///
    /// <para/><b>Reading a citizen never changes what tier they are simulated at.</b> Every path below is a
    /// read; nothing touches <c>Pawn_TierTracker</c>'s notify surface. A drill-down that promoted whoever it
    /// drew would make attention a function of curiosity and leak §11.3's Full-tier budget straight through
    /// the view — <c>CitizenViewTests.Listing_a_settlements_people_changes_nobodys_tier</c> pins it.
    /// </summary>
    public sealed partial class GodViewSnapshot
    {
        /// <summary>
        /// Step one: the people of one settlement, enough of each to pick one out.
        ///
        /// <para/>Returns null when no settlement sits on <paramref name="settlementTile"/> — the same stale-
        /// handle case <see cref="GodCommandOutcome.UnknownSettlement"/> names, reached the same way (a
        /// settlement destroyed between two snapshots). A host holding a <see cref="SettlementSummary.Tile"/>
        /// passes it straight here.
        ///
        /// <para/><b>Cheap enough to call on a settlement.</b> It walks <c>Settlement.Citizens</c> — the
        /// Full/Interval slice plus whoever has been demoted with their pawn intact, never the whole
        /// population — and reads only field reads and cached values per person. The Statistical cohort that
        /// has no <c>Pawn</c> object is never enumerated or materialised; it is reported as a count on
        /// <see cref="SettlementRoster.UnlistedCohortPopulation"/>, because a person with no individual record
        /// has no individual record to show and saying so is the honest answer.
        ///
        /// <para/><b>Measured, not assumed:</b> a settlement of 42,024 people — 2,024 of them with live
        /// <c>Pawn</c> objects, 40,000 a bare cohort — lists in roughly 2.8&#160;ms on the machine this was
        /// written on, and the forty thousand contribute none of it. The per-person cost is dominated by one
        /// uncached <c>MentalBreakThreshold</c> stat evaluation, which is what
        /// <see cref="CitizenLine.MoodBand"/> costs to answer in the simulation's own banding rather than a
        /// scale invented here. That is a view opened on demand, not one drawn every frame: a host that wants
        /// a roster per frame should hold the last one and re-ask when it changes, the same way it already
        /// treats <see cref="GodViewSnapshot"/>.
        /// </summary>
        public static SettlementRoster? CitizensOf(int settlementTile)
        {
            World.Settlement? settlement = AttentionManager.SettlementAt(settlementTile);
            if (settlement == null) return null;

            IReadOnlyList<Pawn> roster = settlement.Citizens;
            var lines = new List<CitizenLine>(roster.Count);
            for (int i = 0; i < roster.Count; i++)
            {
                lines.Add(CitizenLine.Of(roster[i]));
            }

            return new SettlementRoster(
                Find.TickManager.TicksGame,
                settlement.name,
                settlement.tile,
                lines,
                settlement.StatisticalPopulation,
                settlement.TotalPopulation);
        }

        /// <summary>
        /// Step two: one named person, in depth — needs, health, skills, traits, relationships, and their
        /// history. See <see cref="CitizenView"/> for exactly what each tier can and cannot report; the short
        /// version is that a Statistical citizen's identity and history are real, their needs and health are
        /// cohort samples that say so, and their hediffs are not reported at all rather than reported stale.
        ///
        /// <para/><b>Where it looks for them.</b> Every settlement's live roster first, then the corpses
        /// standing on any generated interior — a citizen who died on a map is still a person the player
        /// wants to open, and <c>Settlement.PruneDeadCitizens</c> takes them off the roster on its own rare
        /// cadence. Returns null when neither holds them, which is the truthful answer for somebody who died
        /// unspawned and left no body: nothing in the simulation has their record any more.
        /// <see cref="Remembered"/> is what still answers for them, and <see cref="CitizenLine.Name"/> is the
        /// handle a host keeps for exactly that case.
        ///
        /// <para/><b>This is the expensive one, and it is called for one person.</b> Do not loop it over a
        /// roster; that is what <see cref="CitizensOf"/> exists for.
        /// </summary>
        public static CitizenView? Citizen(int citizenThingId)
        {
            List<Pawn> livePeople = LivePeople();
            for (int i = 0; i < livePeople.Count; i++)
            {
                if (livePeople[i].thingIDNumber == citizenThingId) return CitizenView.Of(livePeople[i], livePeople);
            }

            Pawn? buried = FindInCorpses(citizenThingId);
            return buried == null ? null : CitizenView.Of(buried, livePeople);
        }

        /// <summary>
        /// The civilization's memory of somebody it no longer holds a record of: their name and every
        /// chronicle line and curated moment that names them, and — deliberately — nothing else.
        ///
        /// <para/><b>This is the step that had to survive a death.</b> Frostpunk 2's reviewed failure is that
        /// losing people stopped stinging, and a drill-down that dead-ends the moment somebody dies makes the
        /// same mistake one layer down: the person who mattered most is the one the view can no longer open.
        /// So the path stays walkable past the end of a life. <see cref="CitizenView.IsLive"/> is false on
        /// what comes back, every fidelity is <see cref="CitizenFidelity.NotTracked"/>, and every list but
        /// <see cref="CitizenView.History"/> is empty — a view that states it knows a name and a history and
        /// nothing else, rather than one that fills the shape with zeroes a host would draw as facts.
        ///
        /// <para/>Never null, including for a name the chronicle has never mentioned: an empty
        /// <see cref="CitizenView.History"/> is the true answer to "what does this civilization remember about
        /// this person", and the host says "nothing" rather than being handed an error to interpret. See
        /// <see cref="CitizenView.History"/> for the one real limitation — the chronicle records names and not
        /// ids, so two people who share a name share a history here.
        /// </summary>
        public static CitizenView Remembered(string citizenName)
        {
            if (citizenName == null) throw new ArgumentNullException(nameof(citizenName));
            return CitizenView.Remembering(citizenName);
        }

        /// <summary>
        /// Everybody in the world who still has a <c>Pawn</c> object: every settlement's roster, concatenated.
        /// Bounded by the Full/Interval slice plus whoever was demoted with their pawn intact — never by the
        /// civilization's head count, because the Statistical cohort this deliberately skips has nothing to
        /// enumerate (<c>Settlement.StatisticalPopulation</c> is a bare int, by that tier's own design).
        /// <para/>
        /// Built once per <see cref="Citizen"/> call and handed to <see cref="CitizenView.Of"/>, which needs
        /// the same list again to resolve relationships — building it twice would double the only walk this
        /// query makes.
        /// </summary>
        private static List<Pawn> LivePeople()
        {
            var people = new List<Pawn>();
            World.World? world = Find.World;
            if (world == null) return people;

            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement settlement)) continue;
                IReadOnlyList<Pawn> roster = settlement.Citizens;
                for (int c = 0; c < roster.Count; c++) people.Add(roster[c]);
            }
            return people;
        }

        /// <summary>
        /// The citizen inside a corpse standing on some settlement's interior, or null.
        ///
        /// <para/>Scans <c>ListerThings.AllThings</c> rather than a group, because there is no corpse group to
        /// ask (<c>ThingRequestGroup</c> has none) and inventing one would mean editing a file three other
        /// lanes are in. The cost is one walk of one map's things, paid only by a lookup that already failed
        /// against every roster — which is the rare half of a query that is already the expensive one.
        /// </summary>
        private static Pawn? FindInCorpses(int citizenThingId)
        {
            World.World? world = Find.World;
            if (world == null) return null;

            IReadOnlyList<World.WorldObject> objects = world.worldObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!(objects[i] is World.Settlement settlement)) continue;
                Map.Map? map = settlement.InteriorMap;
                if (map == null) continue;

                IReadOnlyList<Thing> things = map.listerThings.AllThings;
                for (int t = 0; t < things.Count; t++)
                {
                    if (things[t] is Corpse corpse && corpse.InnerPawn != null
                        && corpse.InnerPawn.thingIDNumber == citizenThingId)
                    {
                        return corpse.InnerPawn;
                    }
                }
            }
            return null;
        }
    }
}
