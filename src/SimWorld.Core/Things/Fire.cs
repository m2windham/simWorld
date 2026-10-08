using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// A burning cell, or a burning Thing (RimWorld: <c>Verse.Fire</c>). It grows while there is fuel under
    /// it, damages that fuel, throws sparks at neighbouring cells once big enough, and dies when the fuel
    /// runs out, when rain puts it out, or when somebody beats it out (<c>AI.JobDriver_BeatFire</c>).
    /// <para/>
    /// <b>Where it lives.</b> Nowhere new: a Fire is an ordinary <see cref="Thing"/>, so
    /// <see cref="Map.ListerThings.ThingsOfDef"/> already indexes every fire on a map by def, which is the
    /// registry <c>WorkGiver_FightFires</c> scans. No field was added to <see cref="Map.Map"/> for this.
    /// <para/>
    /// <b>Tick cost.</b> <c>tickerType</c> is <c>Normal</c>, as RimWorld's is, but everything expensive runs
    /// once per <see cref="ComplexCalcsInterval"/> and is spread across that interval by the fire's own id
    /// (this codebase's <c>IsHashIntervalTick</c> idiom, so a burning settlement never does all of its fire
    /// work on one tick). Per-tick cost for a free-standing fire is a null check and one modulo; a fire
    /// riding a pawn adds one cell comparison to follow them. Fires are also self-limiting in a way most
    /// Normal-list tickers are not — a fire that has eaten its fuel destroys itself.
    /// <para/>
    /// <b>Constants.</b> RimWorld's, recalled rather than sourced from a decompile in this sandbox. Per
    /// CLAUDE.md, the tests pin the behaviour they produce — fire grows toward a cap, damage rises with
    /// size, spread needs a grown fire and a flammable target — and never these literals.
    /// </summary>
    public class Fire : AttachableThing
    {
        /// <summary>Smallest a fire can be; below this it is out (RimWorld: <c>Fire.MinFireSize</c>).</summary>
        public const float MinFireSize = 0.1f;

        /// <summary>Largest a fire grows (RimWorld: <c>Fire.MaxFireSize</c>).</summary>
        public const float MaxFireSize = 1.75f;

        /// <summary>Ticks between the growth/damage/spread pass (RimWorld: <c>Fire</c>'s own 150-tick
        /// "complex calcs" cadence). Every per-tick rate below is multiplied by this when it is applied.</summary>
        public const int ComplexCalcsInterval = 150;

        /// <summary>Fire size gained per tick, per point of the hottest fuel's flammability.</summary>
        public const float FireBaseGrowthPerTick = 0.00055f;

        /// <summary>Damage per tick a fire deals to what it is on, before its size is folded in.</summary>
        public const float DamagePerTickBase = 0.0125f;

        /// <summary>Extra damage per tick per point of <see cref="fireSize"/>.</summary>
        public const float DamagePerTickPerFireSize = 0.0036f;

        /// <summary>Ceiling on the per-tick damage the two constants above can reach.</summary>
        public const float MaxDamagePerTick = 0.05f;

        /// <summary>A fire smaller than this never throws sparks (RimWorld: <c>Fire.MinFireSizeToEmitSpark</c>).</summary>
        public const float MinFireSizeToSpread = 1f;

        /// <summary>Ticks between sparks at <see cref="MinFireSize"/>, shortened as the fire grows.</summary>
        public const int TicksBetweenSparksBase = 150;

        /// <summary>Ticks knocked off <see cref="TicksBetweenSparksBase"/> per point of <see cref="fireSize"/>.</summary>
        public const float TicksBetweenSparksReductionPerFireSize = 40f;

        /// <summary>Floor on the spark interval however big the fire gets.</summary>
        public const int MinTicksBetweenSparks = 75;

        /// <summary>A spark goes to one of the eight neighbouring cells this often; otherwise it is thrown
        /// further, into the ring two to three cells out (RimWorld: <c>Fire.TrySpread</c>'s own near/far split).</summary>
        public const float SpreadToAdjacentChance = 0.8f;

        /// <summary>Chance per <see cref="ComplexCalcsInterval"/>, at rain rate 1, that an unroofed fire is
        /// rained out (RimWorld: <c>Fire.BaseSkyExtinguishChance</c>). Driven by
        /// <c>Weather.WeatherManager</c>, on this same interval — see <see cref="TryExtinguishFromRain"/>.</summary>
        public const float BaseSkyExtinguishChance = 0.04f;

        /// <summary>How big a fire is, 0.1 to 1.75. Drives its damage, how fast it spreads, and how many
        /// beats it takes to put out.</summary>
        public float fireSize = MinFireSize;

        private int ticksSinceSpawn;
        private int ticksSinceSpread;

        /// <summary>Flammability of the most flammable thing under this fire, refreshed each complex pass;
        /// 0 means the fuel is gone and the fire is about to die.</summary>
        private float flammabilityMax;

        /// <summary>Reused across passes so a fire allocates nothing per tick. Never saved — rebuilt from the
        /// map on the first pass after a load.</summary>
        private readonly List<Thing> fuel = new List<Thing>();

        public override string InspectStringAddon => "Burning";

        /// <summary>Ticks this fire has been alive; a burn duration other systems can read.</summary>
        public int TicksSinceSpawn => ticksSinceSpawn;

        public override void Tick()
        {
            // AttachableThing follows the parent and dies with it; that can destroy us mid-tick.
            base.Tick();
            if (!Spawned || Destroyed) return;

            ticksSinceSpawn++;
            ticksSinceSpread++;
            // Shared HashInterval, not an open-coded thingIDNumber * 3: ComplexCalcsInterval is 150 and
            // divisible by 3, so the old offset put every fire on one of fifty reachable phases three deep —
            // and a burning settlement is exactly the case this interval exists to spread. See the helper.
            if (HashInterval.IsHashIntervalTick(thingIDNumber, ComplexCalcsInterval))
            {
                DoComplexCalcs();
            }
        }

        /// <summary>A free-standing fire smaller than this neither burns nor sets alight a pawn standing in it
        /// (RimWorld: <c>Fire.MinSizeForIgniteMovables</c>). A pawn still counts as the fire's fuel below it, as
        /// in RimWorld, so a small fire under someone's feet keeps going and grows toward this.</summary>
        public const float MinSizeForIgniteMovables = 0.4f;

        /// <summary>A pawn caught in a fire past <see cref="MinSizeForIgniteMovables"/> is set alight at this
        /// fraction of the fire's size (RimWorld: <c>list[i].TryAttachFire(this.fireSize * 0.2f)</c> in
        /// <c>Fire.DoComplexCalcs</c>).</summary>
        public const float IgniteMovablesSizeFactor = 0.2f;

        /// <summary>
        /// The growth/damage/spread pass (RimWorld: <c>Fire.DoComplexCalcs</c>, restated from the 1.0
        /// decompile, <c>RimWorld/Fire.cs</c>). Gathers what this fire could burn, burns <b>one</b> of those
        /// things, grows, and maybe throws a spark. A fire with nothing flammable left under it goes out here —
        /// which is what stops fire crossing bare ground.
        /// <para/>
        /// <b>What changed, and why it mattered.</b> This pass used to burn <i>every</i> flammable thing in the
        /// cell each time, pawns included, whatever the fire's size — so a fresh spark in a field set alight
        /// the farmer standing in it on its first pass, through <see cref="DamageWorker_Flame"/>. RimWorld burns
        /// one thing chosen at random (or the parent, for a riding fire), and never burns or ignites a pawn
        /// until the fire is past <see cref="MinSizeForIgniteMovables"/>; a pawn has that long to walk out.
        /// Water now puts out a riding fire as well as a standing one — RimWorld's terrain check comes before
        /// its parent branch — which is what <see cref="AI.JobGiver_JumpInWater"/> runs for.
        /// </summary>
        private void DoComplexCalcs()
        {
            Map.Map map = Map!;
            fuel.Clear();
            flammabilityMax = 0f;

            // RimWorld: TerrainDef.extinguishesFire. This port has no such flag; water terrain is the whole of
            // what carries it here, and TerrainDef.IsWater already names that set. Ahead of the parent branch,
            // as RimWorld's is: a burning pawn who reaches water is put out on the fire's next pass.
            if (!GenGrid.GetTerrain(Position, map).IsWater)
            {
                if (parent == null)
                {
                    IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(Position);
                    // Live count, as RimWorld's loop is: setting a pawn alight below spawns a fire into this
                    // very cell, which is appended and skipped.
                    for (int i = 0; i < here.Count; i++)
                    {
                        Thing t = here[i];
                        if (ReferenceEquals(t, this) || t is Fire) continue;
                        float flammability = FireUtility.FlammabilityOf(t);
                        if (flammability < FireUtility.MinFlammability) continue;
                        fuel.Add(t);
                        if (flammability > flammabilityMax) flammabilityMax = flammability;
                        if (fireSize > MinSizeForIgniteMovables && t.def.category == ThingCategory.Pawn)
                        {
                            t.TryAttachFire(fireSize * IgniteMovablesSizeFactor);
                        }
                    }
                }
                else
                {
                    // An attached fire burns exactly one thing: whatever it is riding.
                    fuel.Add(parent);
                    flammabilityMax = FireUtility.FlammabilityOf(parent);
                }
            }

            if (flammabilityMax < FireUtility.MinFlammability)
            {
                Destroy();
                return;
            }

            Thing? target = parent ?? (fuel.Count > 0 ? fuel[Rand.Range(0, fuel.Count)] : null);
            if (target != null
                && (fireSize >= MinSizeForIgniteMovables || ReferenceEquals(target, parent) || target.def.category != ThingCategory.Pawn))
            {
                DoFireDamage(target);
                // Killing the parent takes this fire with it (AttachableThing.Tick), and a fire whose own
                // cell has been emptied has nothing more to do this pass either.
                if (!Spawned || Destroyed) return;
            }

            fireSize += FireBaseGrowthPerTick * flammabilityMax * ComplexCalcsInterval;
            if (fireSize > MaxFireSize) fireSize = MaxFireSize;

            TrySpread();
        }

        /// <summary>
        /// One interval's worth of burning applied to <paramref name="target"/> (RimWorld:
        /// <c>Fire.DoFireDamage</c>). A pawn goes through the ordinary <c>DamageDef.Worker</c> health
        /// pipeline — <see cref="DamageWorker_Flame"/>, which leaves a real <c>Burn</c> injury and can set the
        /// pawn alight — and everything else through the generic hit-points path, the same Pawn/Thing split
        /// <c>GenExplosion</c> and <c>RoofCollapseUtility</c> already use.
        /// </summary>
        private void DoFireDamage(Thing target)
        {
            if (target.Destroyed) return;

            float perTick = GenMath.Clamp(DamagePerTickBase + DamagePerTickPerFireSize * fireSize, 0f, MaxDamagePerTick);
            int amount = GenMath.RoundRandom(perTick * ComplexCalcsInterval, Rand.Current);
            if (amount < 1) amount = 1;

            var dinfo = new DamageInfo(FireDamageDefOf.Flame, amount, instigator: this);
            if (target is Pawn pawn)
            {
                if (pawn.Dead) return;
                FireDamageDefOf.Flame.Worker.Apply(dinfo, pawn);
            }
            else
            {
                target.TakeDamage(dinfo);
            }
        }

        /// <summary>Throws one spark at a nearby cell once the fire is big enough and enough time has passed
        /// (RimWorld: <c>Fire.TrySpread</c>). The draw goes through the seeded stream, so a given seed always
        /// burns the same way.</summary>
        private void TrySpread()
        {
            if (fireSize < MinFireSizeToSpread) return;
            if (ticksSinceSpread < TicksBetweenSparks) return;
            ticksSinceSpread = 0;

            Map.Map map = Map!;
            IntVec3 target = Rand.Chance(SpreadToAdjacentChance)
                ? Position + GenRadial.RadialPattern[Rand.RangeInclusive(1, 8)]
                : Position + GenRadial.RadialPattern[Rand.RangeInclusive(10, 20)];

            if (!GenGrid.InBounds(target, map)) return;
            if (!Rand.Chance(FireUtility.ChanceToStartFireIn(target, map))) return;
            FireUtility.TryStartFireIn(target, map, MinFireSize);
        }

        /// <summary>Ticks this fire waits between sparks; a bigger fire throws them faster, down to a floor.</summary>
        public int TicksBetweenSparks =>
            GenMath.Clamp(
                (int)(TicksBetweenSparksBase - TicksBetweenSparksReductionPerFireSize * fireSize),
                MinTicksBetweenSparks,
                TicksBetweenSparksBase);

        /// <summary>
        /// Rolls this fire's chance of being rained out (RimWorld: the rain branch of
        /// <c>Fire.DoComplexCalcs</c>, which reads <c>Map.weatherManager.RainRate</c> and only ever puts out
        /// a fire under open sky).
        /// <para/>
        /// <b>Wired.</b> It was written before any weather existed and documented here as a gap; the weather
        /// module now drives it. <c>Weather.WeatherManager.WeatherManagerTick</c> calls
        /// <see cref="FireUtility.ExtinguishFiresFromRain"/> — the call site this was waiting on — once per
        /// <see cref="ComplexCalcsInterval"/> while it is raining, so a fire gets exactly one roll per
        /// interval, the same rate RimWorld gives it by rolling inside each fire's own pass.
        /// </summary>
        /// <param name="rainRate">0 (dry) to 1 (downpour).</param>
        /// <returns>True if this call put the fire out.</returns>
        public bool TryExtinguishFromRain(float rainRate)
        {
            if (!Spawned || Destroyed) return false;
            if (rainRate <= 0f) return false;
            if (GenGrid.Roofed(Position, Map!)) return false;
            if (!Rand.Chance(BaseSkyExtinguishChance * rainRate)) return false;
            Destroy();
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref fireSize, "fireSize", MinFireSize);
            Scribe_Values.Look(ref ticksSinceSpawn, "ticksSinceSpawn", 0);
            Scribe_Values.Look(ref ticksSinceSpread, "ticksSinceSpread", 0);
        }
    }
}
