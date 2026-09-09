using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// Data half of a produce-cycle comp (RimWorld: the fields <c>CompProperties_Milkable</c>/
    /// <c>_Shearable</c>/<c>_EggLayer</c> all share). <see cref="resourceDef"/> is what gets spawned,
    /// <see cref="resourceAmount"/> how many, once every <see cref="resourceIntervalDays"/> days.
    /// </summary>
    public abstract class CompProperties_HasGatherableBodyResource : CompProperties
    {
        public ThingDef? resourceDef;
        public int resourceAmount = 1;
        public float resourceIntervalDays = 1f;

        /// <summary>Only a female of the race ever fills this comp (RimWorld: milk/eggs; wool is not).</summary>
        public bool femaleOnly;

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (resourceDef == null) yield return GetType().Name + " has no resourceDef.";
            if (resourceAmount <= 0) yield return GetType().Name + "'s resourceAmount must be positive.";
            if (resourceIntervalDays <= 0f) yield return GetType().Name + "'s resourceIntervalDays must be positive.";
        }
    }

    /// <summary>
    /// Runtime half: fills toward 1 over <see cref="CompProperties_HasGatherableBodyResource.resourceIntervalDays"/>
    /// days, then sits full until <see cref="Gather"/> resets it (RimWorld: <c>CompHasGatherableBodyResource</c>).
    /// Ticks every game tick like any other <see cref="ThingComp"/> on a Full-tier pawn, but the actual gain
    /// only applies on <see cref="HusbandryTuning.ProduceCheckIntervalTicks"/>'s cadence via
    /// <see cref="Pawn.IsHashIntervalTick"/> — a cheap per-tick modulo check, never per-tick real work
    /// (<c>docs/perf/baseline.md</c>).
    /// </summary>
    public abstract class CompHasGatherableBodyResource : ThingComp
    {
        public float fullness;

        public CompProperties_HasGatherableBodyResource Properties => (CompProperties_HasGatherableBodyResource)props;

        /// <summary>Full, and (for a female-only comp) the parent actually is one.</summary>
        public bool Active => fullness >= 1f && (!Properties.femaleOnly || IsFemale);

        private bool IsFemale => !(parent is Pawn pawn) || pawn.gender == Gender.Female;

        public override void CompTick()
        {
            base.CompTick();
            if (fullness >= 1f) return;
            if (!(parent is Pawn pawn) || pawn.Dead || !pawn.Spawned) return;
            if (!pawn.IsHashIntervalTick(HusbandryTuning.ProduceCheckIntervalTicks)) return;

            float gainPerTick = 1f / (Properties.resourceIntervalDays * GenDate.TicksPerDay);
            fullness = GenMath.Clamp01(fullness + gainPerTick * HusbandryTuning.ProduceCheckIntervalTicks);
        }

        /// <summary>
        /// Spawns the accumulated resource at the parent's position and resets <see cref="fullness"/> to 0;
        /// null (no-op) when not <see cref="Active"/> or the parent is not spawned on a map.
        /// </summary>
        public Thing? Gather(Pawn? gatherer)
        {
            if (!Active || !(parent is Pawn pawn) || !pawn.Spawned || pawn.Map == null) return null;

            Thing produced = ThingMaker.MakeThing(Properties.resourceDef!);
            produced.stackCount = Properties.resourceAmount;
            GenSpawn.Spawn(produced, pawn.Position, pawn.Map);
            fullness = 0f;
            gatherer?.skills?.Learn(SkillDefOf.Animals, GatherXp);
            return produced;
        }

        /// <summary>Not RimWorld-sourced; matches this port's other flat per-action XP awards (see
        /// <c>GenRecipe</c>/<c>AnimalTuning</c>).</summary>
        protected virtual float GatherXp => 15f;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fullness, "fullness");
        }
    }
}
