using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// One thing, besides its research, that an era asks of the settlement before the civilization leaves it
    /// for the next (<see cref="EraDef.conditions"/>; <c>docs/design/era-progression.md</c> §2).
    ///
    /// <para/><b>Where the idea comes from.</b> RimWorld has no eras, but it does gate a rise in standing on the
    /// colony's built and lived state rather than on knowledge alone: a royal title is not granted until the
    /// colonist has a throne room and a bedroom that meet the title's room requirements
    /// (<c>RoyalTitleDef.throneRoomRequirements</c> / <c>bedroomRequirements</c>; recalled from the Royalty
    /// defs, not checked against source here). Epoch, the author's earlier game, generalised the same thing as
    /// an era "charter" of population, stock and buildings (<c>docs/research/epoch-inspiration.md</c> §2). An
    /// era turn should read "the town has grown into this age", not "a research queue drained".
    ///
    /// <para/><b>Rules every condition obeys</b> (Epoch's actionable-bar checklist): it reads state the
    /// settlement's own machinery produces, so an unwatched town can clear it; the player has a lever that moves
    /// it (an edict, a blueprint, a zone); and it can go down as well as up, which is why the era a civilization
    /// is in is kept as state (<see cref="ResearchManager.CurrentEra"/>) rather than re-derived every read: a
    /// famine after the turn does not take the age back.
    ///
    /// <para/><b>Measured over every settlement the god has.</b> A civilization with no settlement on a map
    /// (a headless research run, a unit test with no world) has nothing to measure, and is judged on research
    /// alone: <see cref="EraConditionContext.Measurable"/> is false and every condition is met.
    /// </summary>
    public abstract class EraCondition
    {
        /// <summary>Whether the condition holds now.</summary>
        public bool IsMet(EraConditionContext context) => !context.Measurable || Current(context) >= Target;

        /// <summary>The bar the reading must reach.</summary>
        public abstract float Target { get; }

        /// <summary>The reading now, in the same units as <see cref="Target"/>.</summary>
        public abstract float Current(EraConditionContext context);

        /// <summary>A short line for the player: what is asked, in words ("Half the people under a roof").</summary>
        public abstract string Label { get; }

        public virtual IEnumerable<string> ConfigErrors()
        {
            if (Target <= 0f) yield return GetType().Name + ": the target must be above zero.";
        }
    }

    /// <summary>
    /// The settlement's people, measured once per check and shared by every condition of an era, so a gate
    /// reads one consistent picture. Built from every <see cref="World.Settlement"/> in the world with an
    /// interior map: the god's settlements, as <see cref="God.View.GodViewSnapshot"/> counts them.
    /// </summary>
    public sealed class EraConditionContext
    {
        private EraConditionContext(List<World.Settlement> settlements)
        {
            Settlements = settlements;
            for (int i = 0; i < settlements.Count; i++)
            {
                World.Settlement s = settlements[i];
                Map.Map map = s.InteriorMap!;
                IReadOnlyList<Pawns.Pawn> roster = s.Citizens;
                for (int c = 0; c < roster.Count; c++)
                {
                    if (!roster[c].Dead) Citizens++;
                }
                Housed += Building.DwellingUtility.HousedCitizens(s, map);
                StoredNutrition += AI.HuntingInitiative.NutritionAvailable(s, map);
            }
        }

        /// <summary>Settlements with an interior map: the ones whose state can be read.</summary>
        public IReadOnlyList<World.Settlement> Settlements { get; }

        /// <summary>Whether there is anything to measure. False with no world, or no settlement on a map.</summary>
        public bool Measurable => Settlements.Count > 0;

        /// <summary>Living citizens of the measured settlements.</summary>
        public int Citizens { get; }

        /// <summary>Citizens with a bed in their household's finished house
        /// (<see cref="Building.DwellingUtility.HousedCitizens"/>).</summary>
        public int Housed { get; }

        /// <summary>Food on the maps and in the ledgers (<see cref="AI.HuntingInitiative.NutritionAvailable"/>).</summary>
        public float StoredNutrition { get; }

        /// <summary>The context now, from <see cref="Find.World"/>.</summary>
        public static EraConditionContext Now()
        {
            var settlements = new List<World.Settlement>();
            World.World? world = Find.World;
            if (world != null)
            {
                IReadOnlyList<World.WorldObject> objects = world.worldObjects;
                for (int i = 0; i < objects.Count; i++)
                {
                    if (objects[i] is World.Settlement s && s.InteriorMap != null) settlements.Add(s);
                }
            }
            return new EraConditionContext(settlements);
        }
    }

    /// <summary>
    /// A share of the people sleep in their household's own finished house. The first condition of the ladder,
    /// because the first thing a founding band lost to was the weather: a band without a roof by the first heat
    /// wave died of it (<c>docs/research/era-arc.md</c> §10.4).
    /// </summary>
    public sealed class EraCondition_Housed : EraCondition
    {
        /// <summary>0-1.</summary>
        public float share = 0.5f;

        public override float Target => share;

        public override float Current(EraConditionContext context) =>
            context.Citizens == 0 ? 0f : (float)context.Housed / context.Citizens;

        public override string Label => share >= 1f
            ? "Everyone sleeps under their own roof"
            : Math.Round(share * 100f) + "% of the people sleep under their own roof";

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (share > 1f) yield return "EraCondition_Housed: share is a fraction, at most 1.";
        }
    }

    /// <summary>
    /// Food in store for <see cref="days"/> days of what the people eat. A surplus is what frees hands from the
    /// fields for a smith or a scribe, so it is the Agrarian age's way out. Counted the way the hunting gate
    /// counts it: the map and the ledger together.
    /// </summary>
    public sealed class EraCondition_FoodStores : EraCondition
    {
        public float days = 10f;

        public override float Target => days;

        /// <summary>Days of food per head: stored nutrition over what the people eat in a day at hunger rate 1
        /// (<see cref="Needs.Need_Food.BaseFoodFallPerTick"/>). With nobody to feed there is no surplus to speak of.</summary>
        public override float Current(EraConditionContext context) =>
            context.Citizens == 0
                ? 0f
                : context.StoredNutrition / (context.Citizens * Needs.Need_Food.BaseFoodFallPerTick * GenDate.TicksPerDay);

        public override string Label => "Food in store for " + days + " days";
    }

    /// <summary>A number of living citizens: the age asks the town to have grown.</summary>
    public sealed class EraCondition_Population : EraCondition
    {
        public int count = 20;

        public override float Target => count;

        public override float Current(EraConditionContext context) => context.Citizens;

        public override string Label => count + " people";
    }

    /// <summary>A finished building of one of <see cref="things"/> stands in the settlement
    /// (<see cref="World.Settlement.StructureCount"/>).</summary>
    public sealed class EraCondition_Building : EraCondition
    {
        public List<ThingDef> things = new List<ThingDef>();
        public int count = 1;

        /// <summary>The player-facing name of the building ("a smithy"); content writes it, since a list of
        /// defs has no single label.</summary>
        public string? label;

        public override float Target => count;

        public override float Current(EraConditionContext context)
        {
            int built = 0;
            for (int s = 0; s < context.Settlements.Count; s++)
            {
                for (int t = 0; t < things.Count; t++) built += context.Settlements[s].StructureCount(things[t]);
            }
            return built;
        }

        public override string Label =>
            (count == 1 ? "" : count + " x ") + (label ?? (things.Count > 0 ? things[0].label ?? things[0].defName : "a building")) + " standing";

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (things.Count == 0) yield return "EraCondition_Building: things is empty.";
        }
    }
}
