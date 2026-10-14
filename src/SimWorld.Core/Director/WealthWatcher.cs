using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Stats;
using SimWorld.Things;

namespace SimWorld.Director
{
    /// <summary>
    /// What a map's player owns, in silver, counted the way the storyteller counts it (RimWorld:
    /// <c>RimWorld.WealthWatcher</c>, one per <c>Verse.Map</c>). Three terms and a recount cadence:
    /// <list type="bullet">
    /// <item><see cref="WealthItems"/> — the market value of every haulable thing on the map, stack counts
    /// included, and of what the player's pawns hold in their hands and wear and wield.</item>
    /// <item><see cref="WealthBuildings"/> — the market value of every building the player's faction owns, plus
    /// the terrain (<see cref="WealthFloorsOnly"/>).</item>
    /// <item><see cref="WealthPawns"/> — the market value of every pawn of the player's faction on the map:
    /// colonists and tame animals, each priced as an individual
    /// (<see cref="PriceUtility.PawnQualityPriceFactor"/>), a wild animal or a raider not at all.</item>
    /// </list>
    /// <see cref="PlayerWealthForStoryteller"/> is <c>items + 0.5 × buildings + pawns</c>, RimWorld's formula for
    /// a player home (<c>Verse.Map.PlayerWealthForStoryteller</c>): a building is worth only half to the
    /// storyteller, because it is the thing a raider has to get through rather than the thing they came for.
    ///
    /// <para/><b>People are counted twice, and that is RimWorld's.</b> The same person is in
    /// <see cref="WealthPawns"/> here and in the per-colonist term of
    /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/>. This class used to say that would "charge for
    /// them twice" and leave them out; the original never did, and a settlement of twenty-five whose stores sat
    /// under the wealth curve's floor for fifty days drew raids from headcount alone however much it built. See
    /// <see cref="CivilizationTarget.PlayerWealthForStoryteller"/>.
    ///
    /// <para/><b>The count is a recount, not a live sum.</b> It is redone when the last count is more than
    /// <see cref="MinCountInterval"/> ticks old (RimWorld: 5000, two game hours) and not before, so a reading is
    /// an estimate that can lag the map by that long — a wall raised a minute ago is not in it yet.
    /// <see cref="ForceRecount"/> is the other way in, for a caller that needs the answer now (a test; a
    /// host that wants the figure on screen). Nothing here is saved: a map that loads has never been counted,
    /// so its first read counts, and a save never carries a number the map could disagree with.
    ///
    /// <para/><b>What differs from RimWorld, and why.</b>
    /// <list type="number">
    /// <item><b>Fog.</b> RimWorld skips a thing or a floor cell the player has not uncovered. This port has no
    /// fog of war, so every cell counts.</item>
    /// <item><b>Who is "the player".</b> <see cref="FactionManager.OfPlayer"/>, as RimWorld's
    /// <c>Faction.OfPlayer</c>. With no player faction nothing is anyone's, so no building and no pawn counts;
    /// the items still do (RimWorld counts every haulable thing on a home map whoever dropped it).</item>
    /// <item><b>A building's owner</b> is <see cref="Building.Building.faction"/>, set by the builder, because
    /// <see cref="Thing"/> carries no faction here. A ruin's wall has none and does not count.</item>
    /// <item><b>Holders.</b> RimWorld walks every <c>IThingHolder</c> under the map
    /// (<c>GetAllThingsRecursively</c>) and so does this, for the holders this port has: what a pawn carries,
    /// wields and wears; the pawn inside a corpse; and the container of a standing building
    /// (<see cref="IThingHolder"/>, today a grave). A buried colonist therefore keeps their kit in the count,
    /// exactly as in RimWorld, and a body in a grave is itself an item worth nothing. A corpse's gear is counted
    /// only when the dead were the player's, as RimWorld's holder filter does (a raider's rifle on a raider's
    /// body, buried or not, is not the settlement's wealth).</item>
    /// <item><b>Not counted:</b> the health-percentage a damaged thing is worth (RimWorld's
    /// <c>StatPart_Health</c> on market value), and <c>HealthTotal</c>, which nothing here reads.</item>
    /// </list>
    /// </summary>
    public sealed class WealthWatcher
    {
        /// <summary>Ticks a count stays current (RimWorld: <c>WealthWatcher.MinCountInterval</c>, 5000).</summary>
        public const int MinCountInterval = 5000;

        /// <summary>
        /// How much of a building's market value the storyteller counts (RimWorld: the literal 0.5 in
        /// <c>Verse.Map.PlayerWealthForStoryteller</c>). Public because <see cref="SettlementWealth"/> applies
        /// the same weight to a settlement nobody has opened, and the two must never differ.
        /// </summary>
        public const float BuildingsFactorForStoryteller = 0.5f;

        private readonly Map.Map map;

        private float wealthItems;
        private float wealthBuildings;
        private float wealthPawns;
        private float wealthFloorsOnly;
        private float lastCountTick = -99999f;

        public WealthWatcher(Map.Map map)
        {
            this.map = map ?? throw new System.ArgumentNullException(nameof(map));
        }

        /// <summary>Items, buildings and pawns, unweighted: what the map holds, not what the storyteller reads.</summary>
        public float WealthTotal
        {
            get
            {
                RecountIfNeeded();
                return wealthItems + wealthBuildings + wealthPawns;
            }
        }

        public float WealthItems
        {
            get
            {
                RecountIfNeeded();
                return wealthItems;
            }
        }

        /// <summary>Every building of the player's faction, plus <see cref="WealthFloorsOnly"/>.</summary>
        public float WealthBuildings
        {
            get
            {
                RecountIfNeeded();
                return wealthBuildings;
            }
        }

        public float WealthFloorsOnly
        {
            get
            {
                RecountIfNeeded();
                return wealthFloorsOnly;
            }
        }

        public float WealthPawns
        {
            get
            {
                RecountIfNeeded();
                return wealthPawns;
            }
        }

        /// <summary><c>items + 0.5 × buildings + pawns</c> — what the storyteller reads for this map as a player home.</summary>
        public float PlayerWealthForStoryteller
        {
            get
            {
                RecountIfNeeded();
                return wealthItems + wealthBuildings * BuildingsFactorForStoryteller + wealthPawns;
            }
        }

        private void RecountIfNeeded()
        {
            if (Find.TickManager.TicksGame - lastCountTick > MinCountInterval) ForceRecount();
        }

        /// <summary>Counts the map now, whatever the last count's age (RimWorld: <c>WealthWatcher.ForceRecount</c>).</summary>
        public void ForceRecount()
        {
            Faction? player = Find.FactionManager.OfPlayer;

            wealthItems = CalculateWealthItems(player);
            wealthBuildings = 0f;
            wealthPawns = 0f;

            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (player != null && buildings[i] is Building.Building building && ReferenceEquals(building.faction, player))
                {
                    wealthBuildings += MarketValueOf(building);
                }
            }

            wealthFloorsOnly = CalculateWealthFloors();
            wealthBuildings += wealthFloorsOnly;

            if (player != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (IsPlayerPawn(pawns[i], player)) wealthPawns += MarketValueOf(pawns[i]);
                }
            }

            lastCountTick = Find.TickManager.TicksGame;
        }

        /// <summary>A living pawn of <paramref name="player"/>'s faction: a colonist or a tamed animal.</summary>
        internal static bool IsPlayerPawn(Pawn pawn, Faction player) => !pawn.Dead && ReferenceEquals(pawn.faction, player);

        /// <summary>What a thing is worth through the full stat pipeline: quality, a building's cost, a pawn's health and skill.</summary>
        internal static float MarketValueOf(Thing thing) => thing.GetStatValue(StatDefOf.MarketValue);

        /// <summary>
        /// What a pawn carries, wields and wears, at market value times stack (RimWorld:
        /// <c>WealthWatcher.GetEquipmentApparelAndInventoryWealth</c>). The port's pawns have no inventory;
        /// what is in their hands (<see cref="Pawn_CarryTracker.CarriedThing"/>) takes its place, because a thing
        /// being hauled is off the map and would otherwise drop out of the count for as long as the haul lasts.
        /// A thing that is still spawned is skipped: the map's own item list has it already.
        /// </summary>
        public static float GetEquipmentApparelAndInventoryWealth(Pawn pawn)
        {
            float total = 0f;

            IReadOnlyList<ThingWithComps> equipment = pawn.equipment.AllEquipmentListForReading;
            for (int i = 0; i < equipment.Count; i++) total += HeldValue(equipment[i]);

            IReadOnlyList<ThingWithComps> worn = pawn.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++) total += HeldValue(worn[i]);

            Thing? carried = pawn.carryTracker.CarriedThing;
            if (carried != null) total += HeldThingWealth(carried, Find.FactionManager.OfPlayer);

            return total;
        }

        private static float HeldValue(Thing thing) => thing.Spawned ? 0f : MarketValueOf(thing) * thing.stackCount;

        /// <summary>
        /// One thing, wherever it is: its market value times its stack if it is an item, and, if it is a body of
        /// <paramref name="player"/>'s own, what the dead still carry and wear — RimWorld's recursion enters a
        /// corpse only when the pawn inside is the player's, so a raider's rifle on a raider's body is nobody's
        /// wealth and a colonist's is.
        /// </summary>
        private static float HeldThingWealth(Thing thing, Faction? player)
        {
            float total = thing.def.EverHaulable ? MarketValueOf(thing) * thing.stackCount : 0f;
            if (player != null && thing is Corpse corpse && corpse.InnerPawn is Pawn dead && ReferenceEquals(dead.faction, player))
            {
                total += GetEquipmentApparelAndInventoryWealth(dead);
            }
            return total;
        }

        private float CalculateWealthItems(Faction? player)
        {
            float total = 0f;

            IReadOnlyList<Thing> items = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < items.Count; i++)
            {
                total += HeldThingWealth(items[i], player);
            }

            // Whatever a standing building holds inside it — a grave and the body in it. RimWorld's walk
            // enters every holder it meets (GetAllThingsRecursively), so a buried colonist keeps their kit in
            // the count and a thing put away in a container is still the map's wealth. The building need not be
            // the player's: the walk is over the map, not over the faction.
            IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < buildings.Count; i++)
            {
                if (!(buildings[i] is IThingHolder holder)) continue;
                IReadOnlyList<Thing> held = holder.GetDirectlyHeldThings().InnerListForReading;
                for (int k = 0; k < held.Count; k++) total += HeldThingWealth(held[k], player);
            }

            if (player != null)
            {
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    if (IsPlayerPawn(pawns[i], player)) total += GetEquipmentApparelAndInventoryWealth(pawns[i]);
                }
            }

            return total;
        }

        /// <summary>Every cell's terrain at its own market value (RimWorld: <c>CalculateWealthFloors</c>, without the fog test).</summary>
        private float CalculateWealthFloors()
        {
            float total = 0f;
            int n = map.cellIndices.NumGridCells;
            for (int i = 0; i < n; i++)
            {
                total += map.terrainGrid.TerrainAt(map.cellIndices.IndexToCell(i)).marketValue;
            }
            return total;
        }
    }
}
