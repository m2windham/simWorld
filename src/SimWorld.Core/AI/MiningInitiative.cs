using System;
using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.World;

namespace SimWorld.AI
{
    /// <summary>
    /// What one thing the settlement is short of, and that rock can give: the item, and how many more of it
    /// the settlement wants than it has or has already marked rock to produce. See
    /// <see cref="MiningInitiative.WantsOf"/>.
    /// </summary>
    public readonly struct MiningWant
    {
        public MiningWant(ThingDef item, float deficit, bool fromBuildSites)
        {
            Item = item;
            Deficit = deficit;
            FromBuildSites = fromBuildSites;
        }

        /// <summary>The item wanted: an ore (<c>Steel</c>) or a stone chunk (<c>ChunkGranite</c>).</summary>
        public ThingDef Item { get; }

        /// <summary>How many more of it, after everything on hand and everything already marked. A fraction
        /// is meaningful: nine cells marked for a chunk that ten cells are expected to drop leave a tenth of a
        /// chunk wanted, which is one more cell and not ten.</summary>
        public float Deficit { get; }

        /// <summary>True when a Blueprint or Frame on the map is waiting for it (or for blocks cut from
        /// it); false when it is only the standing reserve.</summary>
        public bool FromBuildSites { get; }
    }

    /// <summary>
    /// Translation: <b>a settlement decides for itself what to dig</b>, because there is no player to drag a
    /// box over the rock — the mirror of <see cref="HuntingInitiative"/> for mining, and the piece without
    /// which <see cref="WorkGiver_Miner"/> could not be made to read designations.
    ///
    /// <para/><b>The defect this closes, measured.</b> With no designation layer the giver mined every
    /// mineable edifice on the map: on a seed-777 populated TribalStart world the limestone went from 12,985
    /// cells at day 0 to 242 at day 6, two hundred rocks an hour from the thirty-second, and by day six no
    /// mountain stood anywhere on the map. Making the giver read marks fixes that and, on its own, makes the
    /// game quieter and not better: with nobody to mark anything, nobody mines. Something has to <i>issue</i>
    /// the mark, and that something can only be a reader of settlement state. This is it.
    ///
    /// <para/><b>The rule, and it is one sentence: a settlement digs only for something it can name.</b> What
    /// it can name comes from two places and no others:
    /// <list type="number">
    /// <item><b>What its building sites are waiting for.</b> Every <see cref="Blueprint"/> and
    /// <see cref="Frame"/> on the map still needs material delivered to it; if rock can give that material
    /// the settlement digs for it. An ore is dug directly. Stone blocks are not mined, they are cut from
    /// chunks (<see cref="StonecutterInitiative"/>), so a site short of blocks is a want for the chunks they
    /// are cut from, and a chunk is a want for the rock that drops one — a tenth of the cells, on average
    /// (<c>mineableDropChance</c>). This is the same shortfall
    /// <see cref="WorkGiver_ConstructChopWood.WoodShortfall"/> reads for wood, taken to the same place.</item>
    /// <item><b>A reserve of the ore it can spend, and of the currency.</b>
    /// <see cref="MiningTuning.OreReserve"/> of every ore that some building in content is made of, and of
    /// <see cref="EconomyThingDefOf.Silver"/>, which every trade is paid in. Read off content, so a fourth
    /// ore a building is made of is wanted the day it ships.</item>
    /// </list>
    /// With neither, the settlement marks nothing. This initiative has no appetite for stone of its own: the
    /// settlement's autonomous wall is wood unless blocks are already in hand
    /// (<see cref="StoneWallMaterials.PreferredWallDef"/>), so on its own it never has a stone shortfall, and a
    /// stone wall is a decision somebody else made. That is deliberate. A cost the player can shrug off is not
    /// a decision; the cost of a stone wall is a face of rock somebody now has to quarry.
    ///
    /// <para/><b>Where, and how much.</b> Nearest-first from the settlement — the home area if one exists (the
    /// distance to its nearest cell), otherwise the road hub, the same anchors
    /// <see cref="SettlementConstructionInitiative"/> builds around — and capped: never more than
    /// <see cref="MiningTuning.MaxOutstandingDesignations"/> cells marked at once, the player's counted with
    /// the settlement's. An ore vein is rarely on open ground, so the mark is the vein <i>and the shortest dig
    /// to it</i> from ground the settlement can walk to; plain rock is quarried only where it already faces
    /// open ground, so the settlement takes a face and never tunnels for stone. A cell that holds the roof
    /// up is never marked (<see cref="RoofCollapseUtility.WouldCollapseRoofIfRemoved"/> — the same judgement
    /// the miner applies when it picks the job up). It cannot mark a whole mountain: when the cap is full it
    /// marks nothing, and it marks more only as the marked cells are mined out. It marks only what it can
    /// finish, too: a mark that nobody can currently dig (the roof guard now refuses it) does not count
    /// against the cap, so a stale one cannot starve the rest.
    ///
    /// <para/><b>Read live, never stored.</b> Exactly <see cref="HuntingInitiative"/>'s shape: no state, no
    /// Scribe line. Every answer is re-derived from the map on each pass, and what it decided is the marks,
    /// which are saved with the map. It makes <i>no</i> distinction between its own marks and the player's —
    /// they are one ledger, which is what lets a player cancel a settlement's mark, or dig where the
    /// settlement never would, with the same two commands.
    ///
    /// <para/><b>Deliberately not modelled.</b> That stonecutting keeps a standing bill of blocks
    /// (<see cref="StoneWallMaterials.BlocksWantedOf"/>) is not read here: that figure is a stock the bench
    /// tries to hold, it does not fall as walls are built, and mining to feed it would quarry the mountain by
    /// another route. And a mark is never withdrawn by this class — a want that has gone away leaves the
    /// cells marked, to be dug; with a cap of twenty-four that is a cost of minutes, not of a mountain.
    /// </summary>
    public static class MiningInitiative
    {
        /// <summary>Below this a want is rounding noise, not a want: ten cells of a tenth-of-a-chunk each
        /// come to a hair over or under one chunk in floating point.</summary>
        private const double Epsilon = 1e-4;

        // ---------------------------------------------------------------------------------------------
        // Entry points. Same shape as FarmingInitiative/StonecutterInitiative: a tick the map drives, a
        // gated per-map pass, and the ungated logic public so a test can drive one pass.
        // ---------------------------------------------------------------------------------------------

        /// <summary>One map, self-gating on <see cref="MiningTuning.IntervalTicks"/>. Called once per map per
        /// tick from <see cref="Map.Map.MapTick"/>.</summary>
        public static void TickMap(Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (Find.TickManager.TicksGame % MiningTuning.IntervalTicks != 0) return;
            Run(map);
        }

        /// <summary>The ungated pass, resolving the owning settlement (if any) itself.</summary>
        public static int Run(Map.Map map) => Run(HuntingInitiative.SettlementFor(map), map);

        /// <summary>
        /// The ungated pass against an explicit settlement (null: a map no world owns, where the humanlike
        /// pawns standing on it are the settlement's hands and there is no ledger — the same fallback
        /// <see cref="HuntingInitiative"/> makes). Marks the cells that deliver each want, nearest-first, until
        /// the wants are met or the cap is reached. Returns how many cells it marked.
        /// </summary>
        public static int Run(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int headroom = MiningTuning.MaxOutstandingDesignations - OutstandingDesignations(map);
            if (headroom <= 0) return 0;

            Pawn? worker = ReferenceWorker(settlement, map);
            if (worker == null) return 0;

            List<MiningWant> wants = WantsOf(settlement, map);
            if (wants.Count == 0) return 0;

            var anchor = new Anchor(map);
            int marked = 0;
            for (int i = 0; i < wants.Count && headroom > 0; i++)
            {
                int placed = Designate(map, worker, anchor, wants[i], headroom);
                headroom -= placed;
                marked += placed;
            }
            return marked;
        }

        // ---------------------------------------------------------------------------------------------
        // What is wanted.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Everything this settlement is short of that rock can give, in the order it is dug for: wants a
        /// build site is waiting on first, then the reserve, each group by defName so two runs of one state
        /// mark the same cells. A want with nothing left to dig for (no such rock on the map) is still
        /// reported; marking is where that shows up.
        /// <para/>
        /// <c>deficit = max(site need − on the ground, site need + reserve − on the ground − in the books) −
        /// already marked</c>. The first arm counts the ground alone because a builder can only haul what lies
        /// on the map — a ledger full of steel the builders cannot reach is not steel for the frame, which is
        /// the trap <see cref="HuntingInitiative"/> documents for food. The second counts the books because
        /// a reserve is a statement about what the settlement owns.
        /// </summary>
        public static List<MiningWant> WantsOf(Settlement? settlement, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Dictionary<ThingDef, float> marked = ExpectedFromMarks(map);
            var wants = new List<MiningWant>();

            List<ThingDef> items = MineableItems();
            for (int i = 0; i < items.Count; i++)
            {
                ThingDef item = items[i];
                int siteNeed = SiteNeedFor(map, item);
                int reserve = IsReservedOre(item) ? MiningTuning.OreReserve : 0;
                if (siteNeed <= 0 && reserve <= 0) continue;

                int onGround = OnTheGround(map, item);
                int inBooks = settlement?.StoreCountOf(item) ?? 0;
                float deficit = Math.Max(siteNeed - onGround, siteNeed + reserve - onGround - inBooks);
                // Net of what the marks already standing will give — as a fraction, not floored: floor it and
                // nine of ten cells marked for one chunk reads as "no progress", and the next pass marks ten more.
                deficit -= marked.TryGetValue(item, out float expected) ? expected : 0f;
                if (deficit > Epsilon) wants.Add(new MiningWant(item, deficit, fromBuildSites: siteNeed > onGround));
            }

            wants.Sort((a, b) =>
            {
                if (a.FromBuildSites != b.FromBuildSites) return a.FromBuildSites ? -1 : 1;
                return string.CompareOrdinal(a.Item.defName, b.Item.defName);
            });
            return wants;
        }

        /// <summary>Every distinct item some mineable Def in content gives, by defName.</summary>
        private static List<ThingDef> MineableItems()
        {
            var found = new List<ThingDef>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef? gives = all[i].mineable ? all[i].mineableThing : null;
                if (gives != null && !found.Contains(gives)) found.Add(gives);
            }
            found.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return found;
        }

        /// <summary>
        /// Whether the settlement keeps a standing reserve of <paramref name="item"/>: it is an ore a vein
        /// gives (a <c>mineableScatterCommonality</c> above zero — plain rock is not a reserve), and either
        /// the trade currency or the material of some building in content. Read off content on every call,
        /// not cached, for the reason <see cref="MineableUtility.RandomVeinDef"/> gives.
        /// </summary>
        public static bool IsReservedOre(ThingDef item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            bool isOre = false;
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count && !isOre; i++)
            {
                isOre = all[i].mineable && all[i].mineableScatterCommonality > 0f && ReferenceEquals(all[i].mineableThing, item);
            }
            if (!isOre) return false;

            if (ReferenceEquals(item, EconomyThingDefOf.Silver)) return true;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def.category != ThingCategory.Building || def.entityToBuild != null) continue;
                if (def.CostListCountFor(item) > 0) return true;
            }
            return false;
        }

        /// <summary>
        /// How much of <paramref name="item"/> the build sites on <paramref name="map"/> still need
        /// delivered: what every Blueprint costs and every Frame still lacks, plus, for a stone chunk, the
        /// chunks the blocks cut from it would take (the blocks the sites lack, less the blocks lying on the
        /// ground, divided by what one cut yields). The same ledger as
        /// <see cref="WorkGiver_ConstructChopWood.WoodShortfall"/> before it subtracts anything on hand.
        /// </summary>
        public static int SiteNeedFor(Map.Map map, ThingDef item)
        {
            int need = DirectSiteNeed(map, item);

            IReadOnlyList<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int r = 0; r < recipes.Count; r++)
            {
                RecipeDef recipe = recipes[r];
                if (!StonecutterInitiative.IsStonecutting(recipe) || recipe.ingredients == null || recipe.products == null) continue;
                if (recipe.ingredients.Count != 1 || recipe.products.Count != 1) continue;
                if (!ReferenceEquals(Guild.FixedDefOf(recipe.ingredients[0]), item)) continue;

                ThingDef block = recipe.products[0].thingDef;
                int perCut = recipe.products[0].count;
                if (perCut <= 0) continue;
                int blocksShort = DirectSiteNeed(map, block) - OnTheGround(map, block);
                if (blocksShort <= 0) continue;
                int cuts = (blocksShort + perCut - 1) / perCut;
                need += (int)Math.Ceiling(recipe.ingredients[0].GetBaseCount() * cuts);
            }
            return need;
        }

        private static int DirectSiteNeed(Map.Map map, ThingDef material)
        {
            int needed = 0;
            IReadOnlyList<Thing> blueprints = map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint);
            for (int i = 0; i < blueprints.Count; i++)
            {
                if (blueprints[i] is Blueprint bp) needed += bp.EntityToBuild.CostListCountFor(material);
            }
            IReadOnlyList<Thing> frames = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame);
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i] is Frame f) needed += f.MaterialStillNeeded(material);
            }
            return needed;
        }

        /// <summary>Loose stacks of <paramref name="item"/> anywhere on the map, plus what pawns carry.</summary>
        private static int OnTheGround(Map.Map map, ThingDef item)
        {
            int have = 0;
            IReadOnlyList<Thing> stacks = map.listerThings.ThingsOfDef(item);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (stacks[i].Spawned) have += stacks[i].stackCount;
            }
            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Thing? carried = pawns[i].carryTracker?.CarriedThing;
                if (carried != null && ReferenceEquals(carried.def, item)) have += carried.stackCount;
            }
            return have;
        }

        /// <summary>
        /// What the cells already marked for digging are expected to give, by item: each marked
        /// <see cref="Mineable"/>'s <c>mineableYield × mineableDropChance</c>, nominal (a full recovery — a
        /// poor miner under-delivers, the shortfall reappears as a want on the next pass and is marked then).
        /// The marks counted are everyone's, the player's included: they will yield all the same — except one the
        /// roof guard now refuses, which will not.
        /// </summary>
        private static Dictionary<ThingDef, float> ExpectedFromMarks(Map.Map map)
        {
            var expected = new Dictionary<ThingDef, float>();
            foreach (Designation mark in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine))
            {
                Mineable? rock = MineableUtility.GetFirstMineable(mark.target.Cell, map);
                ThingDef? gives = rock?.def.mineableThing;
                if (rock == null || gives == null) continue;
                // A mark nobody may dig gives nothing, and counting it would hide the want it was meant to meet.
                if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) continue;
                expected[gives] = (expected.TryGetValue(gives, out float so) ? so : 0f)
                    + rock.def.mineableYield * rock.def.mineableDropChance;
            }
            return expected;
        }

        // ---------------------------------------------------------------------------------------------
        // The cap.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Mine marks on <paramref name="map"/> that count against <see cref="MiningTuning.MaxOutstandingDesignations"/>:
        /// those over rock that nobody is currently forbidden from digging. A mark over rock the roof guard
        /// now refuses is not outstanding work — no citizen will take it — and counting it would let a
        /// handful of stale marks starve every want behind them.
        /// </summary>
        public static int OutstandingDesignations(Map.Map map)
        {
            int outstanding = 0;
            foreach (Designation mark in map.designationManager.SpawnedDesignationsOfDef(DesignationDefOf.Mine))
            {
                Mineable? rock = MineableUtility.GetFirstMineable(mark.target.Cell, map);
                if (rock == null) continue;
                if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) continue;
                outstanding++;
            }
            return outstanding;
        }

        // ---------------------------------------------------------------------------------------------
        // Where.
        // ---------------------------------------------------------------------------------------------

        /// <summary>A spawned citizen to ask "can the settlement reach that" of: reachability is a question
        /// about a walker, and the settlement's walkers are its citizens. Null when nobody is standing on the
        /// map — no hands, nothing to mark.</summary>
        private static Pawn? ReferenceWorker(Settlement? settlement, Map.Map map)
        {
            if (settlement != null)
            {
                IReadOnlyList<Pawn> citizens = settlement.Citizens;
                for (int i = 0; i < citizens.Count; i++)
                {
                    Pawn p = citizens[i];
                    if (p.Spawned && !p.Dead && ReferenceEquals(p.Map, map)) return p;
                }
                return null;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawns;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p.Spawned && !p.Dead && p.RaceProps.Humanlike) return p;
            }
            return null;
        }

        /// <summary>Where "nearest" is measured from: the nearest cell of the home area when one exists,
        /// otherwise the road hub (the cell <c>SettlementConstructionInitiative</c> falls back to).</summary>
        private readonly struct Anchor
        {
            private readonly IntVec3[]? homeCells;
            private readonly IntVec3 hub;

            public Anchor(Map.Map map)
            {
                Area home = map.areaManager.Home;
                homeCells = null;
                if (home.TrueCount > 0)
                {
                    var cells = new List<IntVec3>(home.TrueCount);
                    foreach (IntVec3 c in home.ActiveCells) cells.Add(c);
                    homeCells = cells.ToArray();
                }
                hub = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
            }

            public int DistanceSquaredTo(IntVec3 c)
            {
                if (homeCells == null) return (c - hub).LengthHorizontalSquared;
                int best = int.MaxValue;
                for (int i = 0; i < homeCells.Length; i++)
                {
                    int d = (c - homeCells[i]).LengthHorizontalSquared;
                    if (d < best) best = d;
                }
                return best;
            }
        }

        /// <summary>Marks the cells that deliver <paramref name="want"/>; returns how many it marked, never
        /// more than <paramref name="headroom"/>.</summary>
        private static int Designate(Map.Map map, Pawn worker, Anchor anchor, MiningWant want, int headroom)
        {
            var candidates = new List<(Mineable rock, int dist)>();
            IReadOnlyList<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            bool quarry = false;
            for (int d = 0; d < all.Count; d++)
            {
                ThingDef def = all[d];
                if (!def.mineable || !ReferenceEquals(def.mineableThing, want.Item) || def.mineableYield <= 0) continue;
                quarry |= def.isNaturalRock;
                IReadOnlyList<Thing> rocks = map.listerThings.ThingsOfDef(def);
                for (int i = 0; i < rocks.Count; i++)
                {
                    if (!(rocks[i] is Mineable rock) || !rock.Spawned) continue;
                    if (map.designationManager.DesignationAt(rock.Position, DesignationDefOf.Mine) != null) continue;
                    // Plain rock is only ever quarried from a face: a cell with open ground beside it. Checked
                    // before the distance, which costs a walk of the home area, so the thousands of interior
                    // cells are discarded by a handful of lookups each.
                    if (def.isNaturalRock && !HasOpenNeighbour(map, rock.Position)) continue;
                    candidates.Add((rock, anchor.DistanceSquaredTo(rock.Position)));
                }
            }
            candidates.Sort((a, b) =>
            {
                int byDistance = a.dist.CompareTo(b.dist);
                if (byDistance != 0) return byDistance;
                int byZ = a.rock.Position.z.CompareTo(b.rock.Position.z);
                return byZ != 0 ? byZ : a.rock.Position.x.CompareTo(b.rock.Position.x);
            });

            int placed = 0;
            double remaining = want.Deficit;
            for (int i = 0; i < candidates.Count && headroom - placed > 0 && remaining > Epsilon; i++)
            {
                Mineable rock = candidates[i].rock;
                if (RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) continue;

                List<Mineable>? dig = quarry
                    ? (Exposed(map, worker, rock.Position) ? new List<Mineable> { rock } : null)
                    : ShortestDig(map, worker, rock, headroom - placed);
                if (dig == null) continue;

                for (int c = 0; c < dig.Count; c++)
                {
                    if (map.designationManager.AddDesignation(new Designation(dig[c].Position, DesignationDefOf.Mine))) placed++;
                }
                remaining -= rock.def.mineableYield * rock.def.mineableDropChance;
            }
            return placed;
        }

        /// <summary>Whether any of the eight cells around <paramref name="c"/> can be walked on.</summary>
        private static bool HasOpenNeighbour(Map.Map map, IntVec3 c)
        {
            for (int i = 0; i < GenAdj.AdjacentCells.Length; i++)
            {
                IntVec3 n = c + GenAdj.AdjacentCells[i];
                if (GenGrid.InBounds(n, map) && GenGrid.Walkable(n, map)) return true;
            }
            return false;
        }

        /// <summary>Whether the settlement's walkers can stand beside <paramref name="c"/> — the question the
        /// miner itself asks (<see cref="WorkGiver_Miner.HasJobOnThing"/>), asked early.</summary>
        private static bool Exposed(Map.Map map, Pawn worker, IntVec3 c) =>
            HasOpenNeighbour(map, c) && Reachability.CanReach(worker, c, PathEndMode.Touch);

        /// <summary>
        /// The shortest run of rock, four-connected, from <paramref name="target"/> out to a cell the
        /// settlement can already stand beside, target first — or null when there is none within
        /// <paramref name="maxCells"/> or when any cell of it is one the roof guard would refuse. A
        /// breadth-first search that stops at the first exposed cell, so on a vein 15 cells deep it looks at
        /// a few hundred cells and on one at the surface at one.
        /// </summary>
        private static List<Mineable>? ShortestDig(Map.Map map, Pawn worker, Mineable target, int maxCells)
        {
            var parent = new Dictionary<IntVec3, IntVec3> { [target.Position] = IntVec3.Invalid };
            var frontier = new Queue<(IntVec3 cell, int length)>();
            frontier.Enqueue((target.Position, 1));

            while (frontier.Count > 0)
            {
                (IntVec3 cell, int length) = frontier.Dequeue();
                Mineable? here = MineableUtility.GetFirstMineable(cell, map);
                if (here == null) continue;

                if (Exposed(map, worker, cell))
                {
                    var dig = new List<Mineable>(length);
                    for (IntVec3 at = cell; at.IsValid; at = parent[at])
                    {
                        Mineable? rock = MineableUtility.GetFirstMineable(at, map);
                        if (rock == null || RoofCollapseUtility.WouldCollapseRoofIfRemoved(rock)) return null;
                        dig.Add(rock);
                    }
                    dig.Reverse();
                    return dig;
                }

                if (length >= maxCells) continue;
                for (int i = 0; i < GenAdj.CardinalDirections.Length; i++)
                {
                    IntVec3 next = cell + GenAdj.CardinalDirections[i];
                    if (!GenGrid.InBounds(next, map) || parent.ContainsKey(next)) continue;
                    if (MineableUtility.GetFirstMineable(next, map) == null) continue;
                    parent[next] = cell;
                    frontier.Enqueue((next, length + 1));
                }
            }
            return null;
        }
    }
}
