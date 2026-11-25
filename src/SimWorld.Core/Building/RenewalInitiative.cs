using System;
using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>What renewal is doing to a house, for the view (<c>docs/design/era-progression.md</c> §7.8).</summary>
    public enum RenewalStage
    {
        /// <summary>Nothing: it stands, or it is a new house nobody is replacing.</summary>
        None,
        /// <summary>Its replacement is planned and nothing of it stands yet.</summary>
        Planned,
        /// <summary>Its replacement is going up; its household still lives in it.</summary>
        Underway,
        /// <summary>Its household has moved out to its replacement, and it is coming down.</summary>
        Replaced,
    }

    /// <summary>
    /// The town rebuilds itself as it climbs (<c>docs/design/era-progression.md</c> §7): once a day, a settlement
    /// at peace with full stores picks the house most in need of renewal and has <see cref="DwellingPlanner.Renew"/>
    /// build its household a house of the newest rung beside it. A settlement initiative beside
    /// <see cref="SettlementConstructionInitiative"/>, run from inside its pass.
    /// <para/>
    /// <b>What RimWorld gives.</b> Little: buildings there do not age, and a colony renews only by the player's
    /// hand (recalled). The mechanisms it uses are RimWorld's — blueprints, frames, the deconstruct designation,
    /// <see cref="GenConstruct.CanReplace"/>'s replace-in-place — and the decision to use them is this port's,
    /// because the settlement is the player here.
    /// <para/>
    /// <b>It never outranks staying alive.</b> It waits while anybody has no bed, while the stores hold fewer than
    /// <see cref="RenewalTuning.MinFoodDays"/> days of food, and while hostiles are on the map
    /// (<see cref="WaitReason"/>).
    /// <para/>
    /// <b>Scores</b> (§7.4): eras behind (the main term, zero for the current era's own form), crowding, walls below
    /// half their hit points, and a little for age. A house is only a candidate when a newer rung is known, and only
    /// renewed when the stock holds its new house's cost with a reserve to spare. Ties go to the older house.
    /// <para/>
    /// Nothing here draws from <see cref="Rand"/>; everything is read off the map in a fixed order, so a loaded
    /// game renews exactly as the one that was saved would have.
    /// </summary>
    public static class RenewalInitiative
    {
        /// <summary>The daily pass; self-gates on <see cref="RenewalTuning.PassIntervalTicks"/>. Homes first, then
        /// the settlement's standing walls (<see cref="MaterialLadder.UpgradeStanding"/>), both under the same waits.
        /// Returns the house it set out to replace, or null.</summary>
        public static Dwelling? TickSettlement(Settlement settlement, Map.Map map)
        {
            if (Find.TickManager.TicksGame % RenewalTuning.PassIntervalTicks != 0) return null;
            Dwelling? renewed = TryRenewOne(settlement, map);
            if (WaitReason(settlement, map) == null)
            {
                int budget = RenewalTuning.MaxUnderway(LivingCitizens(settlement));
                StructureRenewal.Renew(map, budget);
                MaterialLadder.UpgradeStanding(map, budget);
            }
            return renewed;
        }

        /// <summary>One pass, ungated by the clock: the waits, the budget, the scores, then one renewal at most.</summary>
        public static Dwelling? TryRenewOne(Settlement settlement, Map.Map map)
        {
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null) return null;
            compound.EnsureDwellingIds();
            // Short of beds, only a renewal that is itself the cure goes ahead: a crowded household rebuilt into a
            // house with more beds, new before old, so no bed is lost on the way (renewal lane 2). Waiting it out
            // never ended in a growing town: births and newcomers kept the beds short for good.
            string? wait = WaitReason(settlement, map);
            bool bedsShort = wait == BedsShort;
            if (wait != null && !bedsShort) return null;

            DwellingDef? target = DwellingUtility.PreferredDwellingDef();
            if (target == null) return null;

            List<Dwelling> homes = compound.dwellings;
            int underway = 0, standingHomes = 0;
            for (int i = 0; i < homes.Count; i++)
            {
                if (homes[i].replacedById != 0) underway++;
                if (!homes[i].beingDemolished && homes[i].replacesId == 0) standingHomes++;
            }
            if (underway >= RenewalTuning.MaxUnderway(LivingCitizens(settlement))) return null;
            if (underway >= RenewalTuning.MaxHomesUnderway(standingHomes)) return null;
            if (DwellingUtility.CountUnderway(map) >= DwellingTuning.MaxUnderway) return null;
            if (!Affordable(map, target)) return null;

            var candidates = new List<(Dwelling Home, float Score)>();
            SortedDictionary<int, int> households = DwellingUtility.Households(settlement);
            for (int i = 0; i < homes.Count; i++)
            {
                Dwelling d = homes[i];
                if (!IsCandidate(map, d, target)) continue;
                float score = Score(map, d, target, households);
                if (score > 0f) candidates.Add((d, score));
            }
            // Highest score first; the older house (lower id) on a tie.
            candidates.Sort((a, b) => a.Score != b.Score ? b.Score.CompareTo(a.Score) : a.Home.id.CompareTo(b.Home.id));

            bool plotFree = true;
            for (int i = 0; i < candidates.Count; i++)
            {
                Dwelling d = candidates[i].Home;
                if (bedsShort && !AddsBeds(d, target, households)) continue;
                // The old quarter (§7.6): before a rung's last-but-one house goes, its oldest is kept.
                Dwelling? exemplar = ExemplarToKeep(map, d);
                if (exemplar != null)
                {
                    MarkHeritage(map, exemplar);
                    if (ReferenceEquals(exemplar, d)) continue;
                }
                Dwelling? replacement = null;
                if (plotFree)
                {
                    replacement = DwellingPlanner.Renew(map, d, target);
                    plotFree = replacement != null; // no free plot for one means none for the next either
                }
                // No free plot: rebuild on the house's own, while its household lodges in the dormitory. Never while
                // short of beds, and only while the dormitory has a bed for every one of them.
                if (replacement == null && !bedsShort && CanLodge(settlement, map, d, households))
                {
                    replacement = DwellingPlanner.RenewInPlace(map, d, target);
                }
                if (replacement == null) continue;
                Notify_RenewalStarted(compound);
                return d;
            }
            return null;
        }

        /// <summary><see cref="WaitReason"/> when beds are fewer than citizens. Homes are still renewed then where
        /// the renewal adds beds to a crowded household (<see cref="AddsBeds"/>); stores, benches and walls wait.</summary>
        public const string BedsShort = "not everyone has a bed";

        /// <summary>Why renewal is waiting, or null when it may go ahead (§7.4). With <see cref="BedsShort"/> only the
        /// homes that add beds go ahead.</summary>
        public static string? WaitReason(Settlement settlement, Map.Map map)
        {
            int citizens = LivingCitizens(settlement);
            if (citizens == 0) return "nobody lives here";
            if (map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed).Count < citizens) return BedsShort;
            if (FoodDays(settlement, map, citizens) < FoodDaysWanted()) return "food is short";
            if (HostilesOnMap(settlement, map)) return "enemies are on the map";
            return null;
        }

        /// <summary>The bar for food: <see cref="RenewalTuning.MinFoodDays"/>, or the current era's own food
        /// condition if that asks for less.</summary>
        public static float FoodDaysWanted()
        {
            float days = RenewalTuning.MinFoodDays;
            EraDef? era = Find.ResearchManager.CurrentEra;
            if (era == null) return days;
            for (int i = 0; i < era.conditions.Count; i++)
            {
                if (era.conditions[i] is EraCondition_FoodStores food) days = Math.Min(days, food.days);
            }
            return days;
        }

        /// <summary>Days of food in store per head, read as <see cref="EraCondition_FoodStores"/> reads it.</summary>
        public static float FoodDays(Settlement settlement, Map.Map map, int citizens) =>
            citizens == 0
                ? 0f
                : AI.HuntingInitiative.NutritionAvailable(settlement, map) / (citizens * Needs.Need_Food.BaseFoodFallPerTick * GenDate.TicksPerDay);

        private static bool HostilesOnMap(Settlement settlement, Map.Map map)
        {
            Factions.Faction? own = settlement.faction;
            if (own == null) return false;
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Dead || p.Downed || p.Faction == null) continue;
                if (p.Faction.HostileTo(own)) return true;
            }
            return false;
        }

        private static int LivingCitizens(Settlement settlement)
        {
            int n = 0;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (!citizens[i].Dead) n++;
            }
            return n;
        }

        /// <summary>Whether renewing <paramref name="d"/> as <paramref name="target"/> gives a crowded household more
        /// beds: more members than its beds, and a rung with more.</summary>
        public static bool AddsBeds(Dwelling d, DwellingDef target, SortedDictionary<int, int> households)
        {
            int members = households.TryGetValue(d.householdId, out int n) ? n : 0;
            return members > d.def.BedCount && target.BedCount > d.def.BedCount;
        }

        /// <summary>
        /// Whether <paramref name="d"/>'s household could sleep in the dormitory while its house is rebuilt on the
        /// same plot (<see cref="DwellingPlanner.RenewInPlace"/>): the beds outside every house, less the citizens a
        /// finished house does not already sleep, hold all its members. A household lodges with the town for the
        /// days it takes, as one would with its neighbours; nobody is put out to sleep on the ground for it.
        /// </summary>
        public static bool CanLodge(Settlement settlement, Map.Map map, Dwelling d, SortedDictionary<int, int> households)
        {
            int members = households.TryGetValue(d.householdId, out int n) ? n : 0;
            int dormitory = 0;
            IReadOnlyList<Thing> beds = map.listerThings.ThingsOfDef(ConstructionThingDefOf.Bed);
            for (int i = 0; i < beds.Count; i++)
            {
                if (!DwellingUtility.IsDwellingBed(beds[i])) dormitory++;
            }
            int unhoused = LivingCitizens(settlement) - DwellingUtility.HousedCitizens(settlement, map);
            return dormitory - unhoused >= members;
        }

        /// <summary>A finished, lived-in house, not heritage, not already being replaced, in a lower rung than the
        /// newest the civilization knows.</summary>
        private static bool IsCandidate(Map.Map map, Dwelling d, DwellingDef target)
        {
            if (d.isHeritage || d.replacedById != 0 || d.beingDemolished) return false;
            if (d.householdId == Pawn_RelationsTracker.None || !d.IsComplete(map)) return false;
            return target.rung > d.def.rung;
        }

        /// <summary>Eras the house's form is behind the era the town is in; 0 in the current era's own form.</summary>
        public static int ErasBehind(DwellingDef def)
        {
            EraDef? current = Find.ResearchManager.CurrentEra;
            if (current == null || def.era == null) return 0;
            return Math.Max(0, current.order - def.era.order);
        }

        /// <summary>Whether the house's form is due for renewal: <see cref="RenewalTuning.ObsoleteAfterEras"/> or more
        /// eras behind.</summary>
        public static bool IsObsolete(DwellingDef def) => ErasBehind(def) >= RenewalTuning.ObsoleteAfterEras;

        /// <summary>
        /// §7.4's score. Obsolescence is the main term: one per era behind, once due. Crowding (members past one
        /// per bed) counts only toward a house with more beds. A house whose walls are below half their hit points
        /// adds a little, and so does age. Zero means not due: neither obsolete nor crowded into a bigger rung.
        /// </summary>
        internal static float Score(Map.Map map, Dwelling d, DwellingDef target, SortedDictionary<int, int> households)
        {
            int behind = ErasBehind(d.def);
            float obsolescence = behind >= RenewalTuning.ObsoleteAfterEras ? behind : 0f;

            int beds = Math.Max(1, d.def.BedCount);
            int members = households.TryGetValue(d.householdId, out int n) ? n : 0;
            float crowding = target.BedCount > beds && members > beds ? RenewalTuning.CrowdingWeight * (members - beds) / (float)beds : 0f;
            if (obsolescence == 0f && crowding == 0f) return 0f;

            float condition = WallCondition(map, d) < 0.5f ? RenewalTuning.ConditionWeight : 0f;
            float age = d.completedTick == Building.UnknownBuiltTick
                ? 0f
                : RenewalTuning.AgeWeightPerYear * (Find.TickManager.TicksGame - d.completedTick) / GenDate.TicksPerYear;
            return obsolescence + crowding + condition + age;
        }

        /// <summary>The house's walls' hit points over their most, 0-1; 1 with no wall standing.</summary>
        public static float WallCondition(Map.Map map, Dwelling d)
        {
            int hp = 0, max = 0;
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in d.Parts)
            {
                if (kind != DwellingPartKind.Wall || !GenGrid.InBounds(cell, map)) continue;
                Thing? wall = map.edificeGrid[cell];
                if (wall == null || wall is Frame) continue;
                hp += wall.HitPoints;
                max += wall.MaxHitPoints;
            }
            return max == 0 ? 1f : (float)hp / max;
        }

        /// <summary>
        /// Whether the stock holds the new house's full cost with <see cref="RenewalTuning.MaterialReserve"/> to
        /// spare, over what blueprints and frames already claim (§7.4's gate). Doors and beds are priced in their
        /// default stuff, which they fall back to when nothing else is on hand. Wood is the exception: a builder fells a
        /// tree for a blueprint that wants logs (<see cref="WorkGiver_ConstructChopWood"/>), so logs are got on
        /// demand rather than held, and holding them first would stall every wooden rung behind a woodpile nobody
        /// keeps.
        /// </summary>
        public static bool Affordable(Map.Map map, DwellingDef def)
        {
            var cost = new Dictionary<ThingDef, int>();
            var probe = new Dwelling(def, new CellRect(0, 0, def.Size.x, def.Size.z), Rot4.North);
            foreach ((DwellingPartKind kind, IntVec3 _, Rot4 _) in probe.Parts)
            {
                (ThingDef partDef, ThingDef? stuff) = DwellingUtility.PartDef(map, probe, kind);
                if (partDef.MadeFromStuff) stuff ??= GenStuff.DefaultStuffFor(partDef);
                // A door's or bed's stuff is picked when it is placed, from whatever lies on hand, and falls back to
                // its default when nothing does; priced in what is on hand now, every part claimed the same few
                // steel bars and the house never looked affordable. Priced in its default, as it would be built.
                if (kind != DwellingPartKind.Wall && partDef.MadeFromStuff) stuff = GenStuff.DefaultStuffFor(partDef) ?? stuff;
                List<ThingDefCountClass> list = partDef.CostListAdjusted(stuff);
                for (int i = 0; i < list.Count; i++)
                {
                    cost[list[i].thingDef] = (cost.TryGetValue(list[i].thingDef, out int c) ? c : 0) + list[i].count;
                }
            }
            foreach (KeyValuePair<ThingDef, int> kv in cost)
            {
                if (ReferenceEquals(kv.Key, StuffDefOf.WoodLog)) continue;
                if (MaterialLadder.Spare(map, kv.Key) < (int)Math.Ceiling(kv.Value * RenewalTuning.MaterialReserve)) return false;
            }
            return true;
        }

        // ---- heritage (§7.6) ----

        /// <summary>At most about one structure in <see cref="RenewalTuning.StructuresPerHeritage"/>, counted over
        /// everything the settlement has built, and at least one.</summary>
        public static int HeritageCap(Map.Map map)
        {
            int structures = 0;
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (buildings[i] is Building b && b.Faction != null) structures++;
            }
            return Math.Max(1, structures / RenewalTuning.StructuresPerHeritage);
        }

        public static int HeritageCount(Map.Map map)
        {
            int n = 0;
            IReadOnlyList<Dwelling> homes = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < homes.Count; i++)
            {
                if (homes[i].isHeritage) n++;
            }
            return n;
        }

        /// <summary>
        /// The house of <paramref name="renewing"/>'s rung to keep before it goes, or null: when no house of that
        /// rung is heritage yet and at most two still stand unreplaced (this one and one more, or this one alone), the
        /// oldest of them, while the cap allows another.
        /// </summary>
        private static Dwelling? ExemplarToKeep(Map.Map map, Dwelling renewing)
        {
            IReadOnlyList<Dwelling> homes = DwellingUtility.DwellingsOn(map);
            Dwelling? oldest = null;
            int remaining = 0;
            for (int i = 0; i < homes.Count; i++)
            {
                Dwelling d = homes[i];
                if (!ReferenceEquals(d.def, renewing.def) || d.beingDemolished) continue;
                if (d.isHeritage) return null;
                if (d.replacedById != 0) continue;
                remaining++;
                if (oldest == null || d.id < oldest.id) oldest = d;
            }
            if (remaining > 2 || oldest == null) return null;
            return HeritageCount(map) < HeritageCap(map) ? oldest : null;
        }

        /// <summary>Marks <paramref name="d"/> heritage and remembers what its walls are built of, so a lost wall goes
        /// back as it was (<see cref="DwellingUtility.PartDef"/>). Its buildings are marked too.</summary>
        public static void MarkHeritage(Map.Map map, Dwelling d)
        {
            if (d.replacedById != 0 || d.beingDemolished) return;
            d.isHeritage = true;
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in d.Parts)
            {
                if (!GenGrid.InBounds(cell, map)) continue;
                if (kind == DwellingPartKind.Wall && map.edificeGrid[cell] is Building wall && d.heritageWall == null)
                {
                    d.heritageWall = wall.def;
                    d.heritageWallStuff = wall.Stuff;
                }
                if (map.edificeGrid[cell] is Building b) b.isHeritage = true;
            }
        }

        /// <summary>The god's choice (§7.6): marks or unmarks a house heritage. Unmarking frees it for renewal; it is
        /// repaired in the era's form again.</summary>
        public static void SetHeritage(Map.Map map, Dwelling d, bool heritage)
        {
            if (heritage)
            {
                MarkHeritage(map, d);
                return;
            }
            d.isHeritage = false;
            d.heritageWall = null;
            d.heritageWallStuff = null;
            foreach ((DwellingPartKind _, IntVec3 cell, Rot4 _) in d.Parts)
            {
                if (GenGrid.InBounds(cell, map) && map.edificeGrid[cell] is Building b) b.isHeritage = false;
            }
        }

        // ---- the view and the chronicle ----

        /// <summary>What renewal is doing to <paramref name="d"/>.</summary>
        public static RenewalStage StageOf(Map.Map map, Dwelling d)
        {
            if (d.beingDemolished) return RenewalStage.Replaced;
            if (d.replacedById == 0) return RenewalStage.None;
            Dwelling? replacement = map.settlementCompound?.DwellingById(d.replacedById);
            if (replacement == null) return RenewalStage.None;
            foreach ((DwellingPartKind kind, IntVec3 cell, Rot4 _) in replacement.Parts)
            {
                if (Dwelling.IsBuilt(map, kind, cell)) return RenewalStage.Underway;
            }
            return RenewalStage.Planned;
        }

        /// <summary>One letter per era, on its first renewal: "The town begins to rebuild".</summary>
        private static void Notify_RenewalStarted(SettlementCompound compound)
        {
            EraDef? era = Find.ResearchManager.CurrentEra;
            if (era == null || compound.renewalAnnouncedEraOrder >= era.order) return;
            compound.renewalAnnouncedEraOrder = era.order;
            string text = "The town begins to rebuild in the forms of " + (era.label ?? era.defName)
                + ": the first household's new house is going up beside the old one, which it keeps until it moves.";
            Find.Storyteller.RecordChronicle(text);
            Find.LetterStack.ReceiveLetter("The town rebuilds", text, Letters.LetterDefOf.NeutralEvent);
        }

        /// <summary>A house has come down. When it was the last of its rung that renewal can take, the chronicle
        /// says so, once: "The last pit house is pulled down".</summary>
        internal static void Notify_DwellingDemolished(Map.Map map, Dwelling gone)
        {
            IReadOnlyList<Dwelling> homes = DwellingUtility.DwellingsOn(map);
            bool kept = false;
            for (int i = 0; i < homes.Count; i++)
            {
                if (!ReferenceEquals(homes[i].def, gone.def)) continue;
                if (!homes[i].isHeritage) return;
                kept = true;
            }
            string label = gone.def.label ?? gone.def.defName;
            Find.Storyteller.RecordChronicle(kept
                ? "The last " + label + " in use is pulled down. One is kept, as it was, for the old quarter."
                : "The last " + label + " is pulled down.");
        }
    }
}
