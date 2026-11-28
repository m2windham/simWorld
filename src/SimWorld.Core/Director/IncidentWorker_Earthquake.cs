using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Letters;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Director
{
    /// <summary>
    /// The ground shakes under a settlement (SimWorld's own defect — RimWorld has no earthquake incident to
    /// port; see <c>docs/design/phase-2-pressure.md</c>'s "Known problems": three defects, an earthquake among
    /// them, were blocked on a settlement having nothing to damage. <see cref="World.Settlement.Structures"/>
    /// unblocks all three; this is the first to spend it).
    ///
    /// <para/><b>It lands where the buildings are, whoever is looking.</b> A settlement with an interior map
    /// loses real buildings off that map, watched or not; only one that has never been entered loses them out
    /// of <see cref="World.Settlement.Structures"/> directly — no map generated to stage the loss. That ledger
    /// is not read while a map exists (<see cref="World.Settlement.StructureCount"/>) and is rewritten from the
    /// map on the next sync, so shaking the ledger of an entered but unfocused settlement once credited and
    /// reported a loss of buildings that were all still standing. The unwatched arm is the one this incident
    /// exists for: a threat that only costs something while the player happens to be looking is free to
    /// ignore, and free-to-ignore is the failure mode <c>docs/design/phase-2-pressure.md</c> is about.
    ///
    /// <para/><b>The quake breaks buildings; only a roof hurts people.</b> Watched-map damage is direct
    /// <see cref="Thing.TakeDamage"/> on the structures <see cref="World.Settlement.StructureCount"/> tracks.
    /// A wall that goes is a roof holder leaving the map, so <see cref="Map.EdificeGrid"/> asks RimWorld's own
    /// question of the roof it held (<see cref="Building.RoofCollapseCellsFinder"/>: is a roofed cell now
    /// farther than <see cref="Building.RoofCollapseUtility.RoofMaxSupportDistance"/> from any holder, or cut
    /// off from every holder), and whatever falls lands on what is under it through
    /// <see cref="Building.RoofCollapserImmediate"/>: 15–30 Crush on the head and shoulders for a built roof.
    /// RimWorld has no earthquake, but this is how RimWorld hurts anyone when walls come down. A bed that
    /// breaks under a sleeper whose roof still stands hurts nobody: nothing in RimWorld damages a pawn because
    /// the furniture under them broke. Before (Doug's call, 2026-11-28), a broken bed dealt the roof's hit
    /// itself, so a baby, who spends most of the day in bed and has a 5 HP neck, was the likeliest person in
    /// town to die in a quake whether or not anything fell on them.
    ///
    /// <para/>Deaths are credited through <see cref="DeathLedger.RecordAttributed"/> from the ledger's own
    /// delta across the shake, copying <see cref="SettlementRaidResolver.Resolve"/>'s own approach — never
    /// from this worker's own count of who it hit — so attribution can never exceed the deaths the ledger
    /// actually counted: a settlement that belongs to no registered civilization reaches
    /// <c>Pawn_HealthTracker.Kill</c> and is deliberately not counted there, and this worker must not claim
    /// it anyway.
    ///
    /// <para/><b>Its dice are its own.</b> Every roll comes from <see cref="NamedRand"/>, never the ambient
    /// stream, and the target settlement and the severity are composed <i>before</i> the
    /// <see cref="Ablation.IsDisabled(string?)"/> check — see <see cref="IncidentWorker_ManhunterPack"/>'s own
    /// doc for why that ordering is not optional. A falling roof rolls its hit on the ambient stream
    /// (<see cref="Building.RoofCollapserImmediate"/>), and so does <c>Combat.ArmorUtility.ApplyArmor</c>'s
    /// deflection roll for a clothed citizen, so the whole shake is shielded with
    /// <see cref="Rand.PushState(int)"/> / <see cref="Rand.PopState"/>, seeded from this incident's own stream,
    /// exactly as <see cref="IncidentWorker_ManhunterPack.Generate"/> shields <c>PawnGenerator</c>.
    /// </summary>
    public sealed class IncidentWorker_Earthquake : IncidentWorker
    {
        /// <summary>The stream every roll in this incident comes from, qualified by the tick so two firings in
        /// one game differ while a replay of the same game does not — the same qualification
        /// <see cref="IncidentWorker_ManhunterPack"/>'s own stream name uses.</summary>
        private const string StreamName = "Earthquake";

        /// <summary>
        /// Fraction of each structure type destroyed. RimWorld has no earthquake to source this from;
        /// unsourced per CLAUDE.md, so the tests pin that a quake actually costs a settlement a measurable
        /// share of what it built, never these literals. Never 0 (an earthquake that touched nothing would not
        /// be one) and never 1 (a single quake razing a settlement outright is a wipe this port does not model).
        /// </summary>
        public static readonly FloatRange DestructionFractionRange = new FloatRange(0.15f, 0.45f);

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));

            var civ = parms.target as CivilizationTarget;
            if (civ == null) return false;

            RandomStream rand = NamedRand.For(
                StreamName + "|" + (Find.TickManager?.TicksGame ?? 0).ToString(CultureInfo.InvariantCulture));

            // Composed before anything below is asked to act: which settlement shakes, and how hard, are
            // decided now — so both arms of one seed draw identically and differ only in what happens next.
            // See Ablation. A civilization with no settlement to shake has nothing for this incident to do,
            // watched or not: Structures lives on Settlement, not on the bare CivilizationTarget.
            SimWorld.World.Settlement? settlement = civ.ChooseTargetSettlement(rand);
            if (settlement == null) return false;

            float severity = rand.Range(DestructionFractionRange);

            // Ablated: everything above has happened — the storyteller picked this incident and spent its
            // refire timer, the target and severity are decided — and nothing below will. Structures stays
            // exactly as it was, and the ledgers below are never written to. See Ablation.
            if (Ablation.IsDisabled(def?.defName))
            {
                return true;
            }

            string? source = def?.defName;
            // An entered settlement's buildings are on its map whether or not it has the focus; only a
            // settlement with no map keeps them in the abstract ledger. See the class doc.
            Map.Map? map = ArrivalMapFor(civ, settlement) ?? settlement.InteriorMap;

            if (map != null)
            {
                // Where the god view marks it: the tile, and on the map every place a structure fell. The
                // centre is the first one that fell until something better is known, which is enough to
                // jump to.
                var location = new Letters.EventLocation(settlement.tile);
                var toll = new QuakeToll();
                Dictionary<Pawn, int> people = PeopleOn(map, settlement);
                DeathLedger? ledger = Find.Storyteller?.deaths;
                int deadBefore = ledger?.Total ?? 0;
                int claimedBefore = ledger?.TotalAttributed ?? 0;
                Rand.PushState(rand.Int);
                try
                {
                    using (RoofCollapseCause.During("in the earthquake"))
                    {
                        ShakeMap(map, settlement, severity, rand, location, toll);
                    }
                }
                finally
                {
                    Rand.PopState();
                }
                // Whoever the shake killed died under a roof, which names no killer of its own: what the ledger
                // counted and nothing else claimed is the quake's.
                ledger?.RecordAttributed(source, (ledger.Total - deadBefore) - (ledger.TotalAttributed - claimedBefore));
                toll.CountPeople(people);
                CreditStructures(source, toll.Structures);
                parms.location = location;
                SendLetter(settlement, toll, watched: IsWatched(settlement), location);
                return true;
            }

            var abstractToll = new QuakeToll { PeopleKnown = false };
            ShakeSettlement(settlement, severity, abstractToll);
            CreditStructures(source, abstractToll.Structures);
            parms.location = new Letters.EventLocation(settlement.tile);
            SendLetter(settlement, abstractToll, watched: false, parms.location);
            return true;
        }

        // ---- the unwatched half: the whole reason this incident was unblocked ----

        /// <summary>
        /// Reduces <see cref="World.Settlement.Structures"/> directly, no map generated and none needed — the
        /// abstract twin of <see cref="ShakeMap"/>, the same relationship
        /// <see cref="Building.AbstractSettlementConstruction"/> already has with
        /// <see cref="Building.SettlementConstructionInitiative"/>. No roll: which structures existed and how
        /// many of each is arithmetic over <see cref="World.Settlement.StructureCount"/>, not a draw.
        /// </summary>
        private static void ShakeSettlement(SimWorld.World.Settlement settlement, float severity, QuakeToll toll)
        {
            IReadOnlyList<ThingDef> defs = KnownStructureDefs();
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef structureDef = defs[i];
                int have = settlement.StructureCount(structureDef);
                if (have <= 0) continue;

                int destroy = DestroyCount(have, severity);
                settlement.AddStructure(structureDef, -destroy);
                toll.Fell(structureDef, destroy);
            }
        }

        // ---- the watched half: real Things, on a real map ----

        /// <summary>
        /// Destroys a <paramref name="severity"/> share of each known structure def actually standing on
        /// <paramref name="map"/>, counting into <paramref name="toll"/> those of
        /// <paramref name="settlement"/>'s own (a fallen ruin wall is not counted). No structure hits anyone
        /// itself; a roof whose holders went falls on whoever is under it (see the class doc). Only what is
        /// <paramref name="settlement"/>'s own is laid out again (<see cref="PlaceRebuildBlueprint"/>).
        /// </summary>
        private static void ShakeMap(Map.Map map, SimWorld.World.Settlement settlement, float severity, RandomStream rand, Letters.EventLocation location, QuakeToll toll)
        {
            IReadOnlyList<ThingDef> defs = KnownStructureDefs();
            for (int d = 0; d < defs.Count; d++)
            {
                // A copy: destroying an entry deregisters it from the very list ThingsOfDef returns (mirrors
                // RoofCollapserImmediate.ThingsUnder's own reasoning).
                var standing = new List<Thing>(map.listerThings.ThingsOfDef(defs[d]));
                if (standing.Count == 0) continue;

                int toDestroy = DestroyCount(standing.Count, severity);
                for (int n = 0; n < toDestroy && standing.Count > 0; n++)
                {
                    int index = rand.Range(0, standing.Count);
                    Thing target = standing[index];
                    standing.RemoveAt(index);
                    if (target.Destroyed) continue;

                    // Captured before TakeDamage can invalidate it (DeSpawn resets Position).
                    IntVec3 at = target.Position;
                    Rot4 rot = target.Rotation;
                    ThingDef built = target.def;
                    // Rebuilt in what it was built of, as RimWorld's auto-rebuild passes the destroyed thing's
                    // Stuff, while the map can still finish it (PlaceRebuildBlueprint); and only the settlement's own, as RimWorld rebuilds only the player's buildings
                    // (Building.Destroy: Faction == Faction.OfPlayer). A ruin's wall (no faction) falls and stays
                    // down.
                    ThingDef? builtStuff = target.Stuff;
                    bool rebuild = settlement.IsOwnStructure(target);
                    target.TakeDamage(new DamageInfo(RoofCollapseDefOf.Crush, target.MaxHitPoints));

                    // Credited from whether the Thing is actually gone, never assumed from the hit alone: a
                    // future ThingDef with destroyable=false would otherwise let this claim a destruction that
                    // never happened — the same "never over-claim" discipline the death credit applies.
                    if (!target.Destroyed) continue;
                    // Only the settlement's own count as its structures lost, in the letter and in
                    // ResourceImpactLedger alike: a ruin's wall falls too, but it was never the settlement's, and
                    // telling the player "12 structures come down" for a ruin across the map is a loss they never
                    // had. Same faction test as the rebuild.
                    if (rebuild)
                    {
                        toll.Fell(built, 1);
                        location.AddAffected(at);
                        PlaceRebuildBlueprint(map, built, builtStuff, at, rot);
                    }
                }
            }
        }

        /// <summary>The settlement's own people on the map, alive, with how many injuries each carries now, so
        /// the letter can say who the quake killed or hurt: whoever was under a roof that fell when a wall
        /// went.</summary>
        private static Dictionary<Pawn, int> PeopleOn(Map.Map map, SimWorld.World.Settlement settlement)
        {
            var people = new Dictionary<Pawn, int>();
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead || !p.Spawned || p.Map != map) continue;
                people[p] = Injuries(p);
            }
            return people;
        }

        private static int Injuries(Pawn p)
        {
            int n = 0;
            foreach (Hediff h in p.health.hediffSet.hediffs)
            {
                if (h is Hediff_Injury) n++;
            }
            return n;
        }

        /// <summary>What one earthquake cost: the settlement's own structures that fell, by kind, and who of its
        /// people it killed or hurt. The fourth playtest's letters said "99 structures come down" and nothing
        /// else, which is a number and not news.</summary>
        private sealed class QuakeToll
        {
            public readonly List<(ThingDef Def, int Count)> Fallen = new List<(ThingDef, int)>();
            public readonly List<string> Killed = new List<string>();
            public int Hurt;
            public int Structures;
            public bool PeopleKnown = true;

            public void Fell(ThingDef def, int count)
            {
                if (count <= 0) return;
                Structures += count;
                for (int i = 0; i < Fallen.Count; i++)
                {
                    // Walls of every material are one line: the toll is what fell, not what it was made of.
                    if (Fallen[i].Def == def || (StoneWallMaterials.IsWall(Fallen[i].Def) && StoneWallMaterials.IsWall(def)))
                    {
                        Fallen[i] = (Fallen[i].Def, Fallen[i].Count + count);
                        return;
                    }
                }
                Fallen.Add((def, count));
            }

            public void CountPeople(Dictionary<Pawn, int> before)
            {
                foreach (KeyValuePair<Pawn, int> kv in before)
                {
                    if (kv.Key.Dead) Killed.Add(kv.Key.Label);
                    else if (Injuries(kv.Key) > kv.Value) Hurt++;
                }
            }

            /// <summary>"40 walls, 3 beds and a storage hut".</summary>
            public string FallenText()
            {
                var parts = new List<string>();
                foreach ((ThingDef def, int count) in Fallen.OrderByDescending(f => f.Count))
                {
                    string label = StoneWallMaterials.IsWall(def) ? "wall" : def.label ?? def.defName;
                    parts.Add(count == 1 ? "a " + label : count.ToString(CultureInfo.InvariantCulture) + " " + LabelPlural.For(label, count));
                }
                return JoinAnd(parts);
            }

            /// <summary>"Rasmus and Ridgeback were killed, and 3 more were hurt." / "No one was hurt."</summary>
            public string PeopleText()
            {
                string hurt = Hurt.ToString(CultureInfo.InvariantCulture);
                if (Killed.Count == 0)
                {
                    return Hurt == 0 ? "No one was hurt." : hurt + " of your people " + (Hurt == 1 ? "was" : "were") + " hurt, and no one was killed.";
                }
                string dead = JoinAnd(Killed) + (Killed.Count == 1 ? " was" : " were") + " killed";
                return Hurt == 0 ? dead + "." : dead + ", and " + hurt + " more " + (Hurt == 1 ? "was" : "were") + " hurt.";
            }

            private static string JoinAnd(List<string> parts)
            {
                if (parts.Count == 0) return "";
                if (parts.Count == 1) return parts[0];
                return string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " and " + parts[parts.Count - 1];
            }
        }

        /// <summary>
        /// RimWorld's auto-rebuild: a player building destroyed outright leaves a blueprint for itself where it
        /// stood, so the colony puts it back (RimWorld: <c>Building.Destroy</c>, on <c>KillFinalize</c>, when
        /// <c>PlaySettings.autoRebuild</c> is on and the building is the player's, through
        /// <c>GenConstruct.PlaceBlueprintForBuild</c>). <see cref="ShakeMap"/> calls it only for the settlement's
        /// own buildings, by the faction test the structure ledger and repair already use
        /// (<see cref="SimWorld.World.Settlement.IsOwnStructure"/>): a quake also brings down the ruin walls
        /// <see cref="KnownStructureDefs"/> matches, and nobody rebuilds a ruin. Skipped where no blueprint is authored for the def or the cell can no
        /// longer take one. <see cref="Building.SettlementConstructionInitiative"/> counts blueprints toward what
        /// the settlement wants, so it does not also lay a second one elsewhere.
        /// </summary>
        private static void PlaceRebuildBlueprint(Map.Map map, ThingDef built, ThingDef? stuff, IntVec3 at, Rot4 rot)
        {
            // A stone wall (a Def of its own, of no stuff) whose stone is no longer to spare goes back up as the wall
            // the town would raise today, as a new one would: kept in granite once the granite was gone, it waited
            // for ever (renewal lane 2).
            if (stuff == null && StoneWallMaterials.IsWall(built) && !StoneWallMaterials.CanAffordOne(map, built))
            {
                built = StoneWallMaterials.PreferredWallDef(map);
                if (built.MadeFromStuff) stuff = Crafting.GenStuff.DefaultStuffFor(built);
            }
            ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(built);
            if (blueprintDef == null || !GenConstruct.CanPlaceBlueprintAt(built, at, map, out _, rot)) return;
            var blueprint = (Blueprint)ThingMaker.MakeThing(blueprintDef);
            // In its own stuff while the map can still finish it, else in what the settlement has — the choice a
            // new placement makes (SettlementConstructionInitiative.PlaceBlueprint). A wooden bed on a map with no
            // wood left is rebuilt in stone rather than waiting forever. A wall keeps its own stuff, as a new
            // wall does: StoneWallMaterials decides a wall's stuff by research, and a fallback would skip that.
            blueprint.stuffToUse = stuff == null || StoneWallMaterials.IsWall(built)
                ? stuff
                : Crafting.GenStuff.AffordableStuffFor(map, built, stuff);
            GenSpawn.Spawn(blueprint, at, map, rot);
        }

        // ---- shared arithmetic ----

        /// <summary>Every structure def this incident (and <see cref="Building.AbstractSettlementConstruction"/>
        /// before it) knows how to count: every wall material, plus the two other completed buildings a
        /// settlement raises, and a pit house's earth bank. Not read off <see cref="World.Settlement.Structures"/> directly — every consumer
        /// goes through <see cref="World.Settlement.StructureCount"/>, per that method's own doc — so this
        /// names the defs rather than enumerating whatever the ledger happens to hold.</summary>
        private static IReadOnlyList<ThingDef> KnownStructureDefs()
        {
            var defs = new List<ThingDef>(StoneWallMaterials.AllWallDefs)
            {
                ConstructionThingDefOf.Bed,
                ConstructionThingDefOf.StorageHut,
            };
            // A house is walls and beds now (shelter lane), already on this list but for a pit house's dug bank.
            defs.Add(Building.ShelterThingDefOf.WallEarth);
            return defs;
        }

        /// <summary>How many of <paramref name="have"/> to destroy at <paramref name="severity"/>: at least one
        /// whenever there is anything to lose (an earthquake that always spares the last building whatever its
        /// severity would be no earthquake at all), rounded up rather than down for the same reason, and never
        /// more than there actually are.</summary>
        private static int DestroyCount(int have, float severity)
        {
            if (have <= 0) return 0;
            int destroy = (int)Math.Ceiling(have * severity);
            return Math.Clamp(destroy, 1, have);
        }

        /// <summary>Credits the structures actually destroyed to <see cref="ResourceImpactLedger"/>, at the
        /// exact point they were destroyed — never by differencing two runs. See
        /// <see cref="ResourceImpactLedger.RecordStructuresDestroyed"/>.</summary>
        private static void CreditStructures(string? source, int destroyed)
        {
            if (destroyed <= 0) return;
            Find.Storyteller?.resourceImpact.RecordStructuresDestroyed(source, destroyed);
        }

        // ---- shared with ManhunterPack in rule, not in code (CLAUDE.md: add a file, don't share one) ----

        /// <summary>The map this quake physically shakes, or null when it must resolve abstractly — the same
        /// question <see cref="IncidentWorker_ManhunterPack.ArrivalMapFor"/> asks, of the same authority.</summary>
        private static Map.Map? ArrivalMapFor(CivilizationTarget civ, SimWorld.World.Settlement? settlement)
        {
            if (settlement == null) return civ.MapFor(null);
            return IsWatched(settlement) ? civ.MapFor(settlement) : null;
        }

        private static bool IsWatched(SimWorld.World.Settlement settlement) =>
            Find.God.Attention.FocusedTile == settlement.tile;

        /// <summary>The player is told either way — an earthquake nobody is told about is indistinguishable
        /// from nothing happening, and a defect that costs nothing when unwatched teaches the player nothing
        /// about where to look next time.</summary>
        private static void SendLetter(SimWorld.World.Settlement settlement, QuakeToll toll, bool watched, Letters.EventLocation location)
        {
            string where = string.IsNullOrEmpty(settlement.name) ? "a settlement" : settlement.name;
            string fell = toll.Structures == 0
                ? "Nothing of yours comes down."
                : toll.Structures.ToString(CultureInfo.InvariantCulture) + (toll.Structures == 1 ? " structure comes down: " : " structures come down: ")
                    + toll.FallenText() + ".";
            string people = toll.PeopleKnown ? " " + toll.PeopleText() : "";
            string text = watched
                ? "An earthquake tears through " + where + ". " + fell + people
                : "Word reaches you late: an earthquake struck " + where + " while your attention was elsewhere. " + fell + people;

            Letters.Letter? letter = Find.LetterStack?.ReceiveLetter("Earthquake: " + where, text, LetterDefOf.NegativeEvent);
            if (letter != null) letter.location = location;
        }
    }
}
