using System;
using System.Globalization;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// Anything that can occupy a map cell (RimWorld: <c>Verse.Thing</c>). Pawns, buildings, items and
    /// plants are all one of these; <see cref="SimWorld.Pawns.Pawn"/> is the pawn-shaped subclass.
    /// Unspawned by default — <see cref="GenSpawn.Spawn"/>/<see cref="SpawnSetup"/> puts it on a
    /// <see cref="Map.Map"/>, <see cref="DeSpawn"/> takes it back off.
    /// </summary>
    public class Thing : IExposable, ILoadReferenceable, ITickable
    {
        private static int nextThingId;

        public ThingDef def = null!;
        public int thingIDNumber = -1;

        /// <summary>How many identical items this represents in one stack (buildings and pawns stay at 1).</summary>
        public int stackCount = 1;

        public int HitPoints;

        private IntVec3 position = IntVec3.Invalid;
        private Rot4 rotation = Rot4.North;
        private Map.Map? map;
        private bool destroyed;

        public Thing()
        {
        }

        public static int AllocateThingId() => nextThingId++;

        /// <summary>Tests and loaders that mint ids elsewhere reset the counter with this.</summary>
        public static void ResetThingIdCounter(int next = 0) => nextThingId = next;

        public string ThingID => def.defName + thingIDNumber.ToString(CultureInfo.InvariantCulture);

        public virtual string Label => def.label ?? def.defName;

        // ---- map presence ----

        /// <summary>Set by <see cref="GenSpawn.Spawn"/> before <see cref="SpawnSetup"/> registers the Thing.</summary>
        public IntVec3 Position
        {
            get => position;
            internal set => position = value;
        }

        public Rot4 Rotation
        {
            get => rotation;
            internal set => rotation = value;
        }

        public Map.Map? Map => map;

        public bool Spawned => map != null;

        public bool Destroyed => destroyed;

        /// <summary>Cells this Thing's footprint covers at its current position and rotation.</summary>
        public CellRect OccupiedRect() => GenAdj.OccupiedRect(position, rotation, def.size);

        public virtual int MaxHitPoints => def.BaseMaxHitPoints;

        /// <summary>
        /// Puts this Thing on <paramref name="map"/> at its current <see cref="Position"/>/<see cref="Rotation"/>
        /// and wires it into the map's grids (RimWorld: <c>Verse.Thing.SpawnSetup</c>). Callers normally reach
        /// this through <see cref="GenSpawn.Spawn"/>, which also sets position and rotation first.
        /// </summary>
        public virtual void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            this.map = map;
            map.listerThings.Add(this);
            map.thingGrid.Register(this);
            if (def.IsEdifice) map.edificeGrid.Register(this);
            if (this is SimWorld.Pawns.Pawn pawn) map.mapPawns.RegisterPawn(pawn);
            foreach (IntVec3 c in OccupiedRect().Cells)
            {
                map.pathGrid.RecalculatePerceivedPathCostAt(c);
            }
            if (TickerType != TickerType.Never)
            {
                Find.TickManager.RegisterAllTickabilityFor(this);
            }
        }

        /// <summary>Takes this Thing off its map without destroying it (RimWorld: <c>Verse.Thing.DeSpawn</c>).</summary>
        public virtual void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            if (!Spawned) return;
            Map.Map m = map!;
            CellRect occupied = OccupiedRect();
            if (TickerType != TickerType.Never)
            {
                Find.TickManager.DeRegisterAllTickabilityFor(this);
            }
            if (this is SimWorld.Pawns.Pawn pawn) m.mapPawns.DeRegisterPawn(pawn);
            if (def.IsEdifice) m.edificeGrid.DeRegister(this);
            m.thingGrid.Deregister(this);
            m.listerThings.Remove(this);
            map = null;
            position = IntVec3.Invalid;
            foreach (IntVec3 c in occupied.Cells)
            {
                m.pathGrid.RecalculatePerceivedPathCostAt(c);
            }
        }

        /// <summary>Removes this Thing from the sim entirely: de-spawns first if it was on a map.</summary>
        public virtual void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (destroyed) return;
            if (Spawned) DeSpawn(mode);
            destroyed = true;
        }

        /// <summary>
        /// Applies damage by simple hit points (RimWorld: full injury/hediff damage for pawns; this is the
        /// generic non-pawn path). Things with <c>useHitPoints=false</c> are immune; a lethal hit destroys
        /// with <see cref="DestroyMode.KillFinalize"/> when <c>def.destroyable</c>.
        /// </summary>
        public virtual DamageResult TakeDamage(DamageInfo dinfo)
        {
            if (dinfo == null) throw new ArgumentNullException(nameof(dinfo));
            var result = new DamageResult();
            if (destroyed || !def.useHitPoints || dinfo.Amount <= 0f) return result;

            int amount = (int)Math.Round(dinfo.Amount, MidpointRounding.AwayFromZero);
            if (amount <= 0) amount = 1;
            HitPoints -= amount;
            result.totalDamageDealt = amount;
            result.wounded = true;

            if (HitPoints <= 0)
            {
                HitPoints = 0;
                if (def.destroyable) Destroy(DestroyMode.KillFinalize);
            }
            return result;
        }

        /// <summary>Finishes constructing a freshly made Thing (RimWorld: <c>Verse.Thing.PostMake</c>); id and def are already set.</summary>
        public virtual void PostMake()
        {
            HitPoints = MaxHitPoints;
        }

        // ---- ITickable ----

        public int TickId => thingIDNumber;
        public TickerType TickerType => def.tickerType;

        public virtual void Tick()
        {
        }

        public virtual void TickRare()
        {
        }

        public virtual void TickLong()
        {
        }

        // ---- Scribe ----

        public string GetUniqueLoadID() => "Thing_" + ThingID;

        /// <summary>
        /// Saves def/id/position/rotation/stack/hit points. Map presence is not saved here: the map that
        /// owns this Thing re-links it via <see cref="SpawnSetup"/> during its own load.
        /// </summary>
        public virtual void ExposeData()
        {
            ThingDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref thingIDNumber, "id", -1);

            int posX = position.x, posY = position.y, posZ = position.z;
            Scribe_Values.Look(ref posX, "posX", IntVec3.Invalid.x);
            Scribe_Values.Look(ref posY, "posY", IntVec3.Invalid.y);
            Scribe_Values.Look(ref posZ, "posZ", IntVec3.Invalid.z);
            position = new IntVec3(posX, posY, posZ);

            int rotInt = rotation.AsInt;
            Scribe_Values.Look(ref rotInt, "rot", Rot4.North.AsInt);
            rotation = new Rot4(rotInt);

            Scribe_Values.Look(ref stackCount, "stackCount", 1);
            Scribe_Values.Look(ref HitPoints, "hitPoints", MaxHitPoints);

            if (Scribe.mode == LoadSaveMode.LoadingVars && thingIDNumber >= nextThingId)
            {
                nextThingId = thingIDNumber + 1;
            }
        }

        public override string ToString() => Label + " (" + ThingID + ")";
    }
}
