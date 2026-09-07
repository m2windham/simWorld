using System;
using System.Collections.Generic;
using SimWorld.Factions;
using SimWorld.Needs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Caravans
{
    /// <summary>
    /// A group of pawns travelling together on the world map (RimWorld: <c>RimWorld.Planet.Caravan</c>).
    /// <see cref="StartJourney"/> plans a route with <see cref="WorldPathFinder"/>; <see cref="Tick"/> (via
    /// <see cref="World.WorldTick"/>) steps along it one tick at a time and drains <see cref="foodStock"/>
    /// at the travellers' combined hunger rate. Resting at night and caravan forming/disbanding UI flows
    /// are out of scope — this is the tick-level travel mechanic only.
    /// </summary>
    public class Caravan : WorldObject
    {
        public List<Pawn> pawns = new List<Pawn>();

        /// <summary>Food units in reserve; drained each tick by the travellers' combined hunger (see <see cref="ConsumeFood"/>). Not itself tied to any Thing yet — that lands with the Things/Crafting systems.</summary>
        public float foodStock;

        private List<int>? path;
        private int pathIndex;
        private int destinationTile = -1;

        /// <summary>Ticks remaining to finish crossing the edge from <c>path[pathIndex]</c> to <c>path[pathIndex + 1]</c>.</summary>
        private float nextTileCostLeft;

        /// <summary>Raised once, when the caravan reaches its destination tile.</summary>
        public event Action<Caravan>? Arrived;

        /// <summary>For Scribe's deep-load construction.</summary>
        public Caravan()
        {
        }

        public Caravan(WorldObjectDef def, int tile, Faction? faction) : base(def, tile, faction)
        {
        }

        public IReadOnlyList<Pawn> Pawns => pawns;

        public bool Travelling => path != null;

        public int? Destination => destinationTile >= 0 ? destinationTile : (int?)null;

        public IReadOnlyList<int>? Path => path;

        public float NextTileCostLeft => nextTileCostLeft;

        public void AddPawn(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawns.Contains(pawn)) pawns.Add(pawn);
        }

        public bool RemovePawn(Pawn pawn) => pawn != null && pawns.Remove(pawn);

        /// <summary>
        /// Plans a route to <paramref name="destinationTile"/> via <see cref="WorldPathFinder"/> and starts
        /// moving; returns false (and leaves any current journey untouched) when no route exists.
        /// </summary>
        public bool StartJourney(WorldGrid grid, int destinationTile)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));

            if (destinationTile == tile)
            {
                path = null;
                pathIndex = 0;
                this.destinationTile = -1;
                return true;
            }

            if (!WorldPathFinder.FindPath(grid, tile, destinationTile, out List<int> found) || found.Count < 2)
            {
                return false;
            }

            path = found;
            pathIndex = 0;
            this.destinationTile = destinationTile;
            nextTileCostLeft = TicksForEdge(grid, path[0], path[1]);
            return true;
        }

        private float TicksForEdge(WorldGrid grid, int from, int to)
        {
            float difficulty = WorldPathGrid.MovementCostBetween(grid, from, to);
            int ticksPerMove = CaravanTicksPerMoveUtility.GetTicksPerMove(pawns);
            return ticksPerMove * difficulty;
        }

        public override void Tick(global::SimWorld.World.World world)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            CaravanTick(world.grid);
        }

        /// <summary>Advances one tick: drains food, then (if travelling) counts down the current edge and steps/arrives as it reaches zero.</summary>
        public void CaravanTick(WorldGrid grid)
        {
            ConsumeFood();

            if (path == null || pathIndex >= path.Count - 1) return;

            nextTileCostLeft -= 1f;
            while (nextTileCostLeft <= 0f)
            {
                pathIndex++;
                tile = path[pathIndex];

                if (pathIndex >= path.Count - 1)
                {
                    path = null;
                    pathIndex = 0;
                    destinationTile = -1;
                    nextTileCostLeft = 0f;
                    Arrived?.Invoke(this);
                    return;
                }

                float overshoot = -nextTileCostLeft;
                nextTileCostLeft = TicksForEdge(grid, path[pathIndex], path[pathIndex + 1]) - overshoot;
            }
        }

        private void ConsumeFood()
        {
            if (pawns.Count == 0) return;
            float consumed = 0f;
            for (int i = 0; i < pawns.Count; i++)
            {
                consumed += pawns[i].HungerRate * Need_Food.BaseFoodFallPerTick;
            }
            foodStock = Math.Max(0f, foodStock - consumed);
        }

        public override void ExposeData()
        {
            base.ExposeData();

            List<Pawn>? p = pawns;
            Scribe_Collections.Look(ref p, "pawns", LookMode.Deep);
            pawns = p ?? new List<Pawn>();

            Scribe_Values.Look(ref foodStock, "foodStock");
            Scribe_Values.Look(ref destinationTile, "destinationTile", -1);
            Scribe_Values.Look(ref pathIndex, "pathIndex", 0);
            Scribe_Values.Look(ref nextTileCostLeft, "nextTileCostLeft", 0f);

            List<int>? pathList = path;
            Scribe_Collections.Look(ref pathList, "path", LookMode.Value);
            path = pathList;
        }
    }
}
