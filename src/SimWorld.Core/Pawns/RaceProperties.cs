using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Pawns
{
    /// <summary>Cognitive tier; gates which needs, thoughts and breaks apply (RimWorld: <c>Verse.Intelligence</c>).</summary>
    public enum Intelligence
    {
        Animal,
        ToolUser,
        Humanlike,
    }

    /// <summary>
    /// Species-level facts on a pawn's ThingDef (RimWorld: <c>Verse.RaceProperties</c>). Only the fields the
    /// ported systems read so far; later modules extend it (body, life stages, diet, combat).
    /// </summary>
    public class RaceProperties
    {
        public Intelligence intelligence = Intelligence.Humanlike;

        /// <summary>The part tree this race is built from; every race needs one.</summary>
        public BodyDef? body;

        /// <summary>Multiplies every part's hit points.</summary>
        public float baseHealthScale = 1f;

        /// <summary>Scales food capacity, hit points and more.</summary>
        public float baseBodySize = 1f;

        /// <summary>Nutrition burned per day is 1.6 × this.</summary>
        public float baseHungerRate = 1f;

        /// <summary>Food level below which the pawn wants to eat; half of it is "urgently hungry".</summary>
        public float foodLevelPercentageWantEat = 0.3f;

        public bool needsRest = true;

        /// <summary>Flesh creatures bleed, feel pain and get sick; mechanoids do not.</summary>
        public bool isFlesh = true;

        public float lifeExpectancy = 80f;

        /// <summary>This race's life stages by minimum biological age (years), ascending; see <see cref="Pawn_AgeTracker.CurLifeStage"/>.</summary>
        public List<LifeStageAge>? lifeStageAges;

        /// <summary>Distribution <see cref="Generation.PawnGenerator"/> samples a humanlike pawn's biological age from.</summary>
        public SimpleCurve? ageGenerationCurve;

        // ---- Animals module (system: ai.animals/crafting.animals) ----

        /// <summary>How hard this race is to tame and how readily an untamed individual flees or turns on a
        /// handler, 0 (trivial) to 1 (extreme) — RimWorld: <c>RaceProperties.wildness</c>. Meaningless for a
        /// Humanlike race, which is why it defaults to 0 rather than being required.</summary>
        public float wildness;

        /// <summary>Which <see cref="TrainableDef"/>s this race can ever learn (RimWorld: <c>RaceProperties.trainability</c>);
        /// null (the Humanlike default) means none — nothing here is trainable in the <see cref="Pawn_TrainingTracker"/> sense.</summary>
        public TrainabilityDef? trainability;

        /// <summary>What a butchered individual of this race yields as meat (RimWorld: <c>RaceProperties.meatDef</c>);
        /// null falls back to the generic meat item — see <c>Recipe_ButcherAnimal</c>.</summary>
        public ThingDef? meatDef;

        /// <summary>What a butchered individual yields as leather (RimWorld: <c>RaceProperties.leatherDef</c>);
        /// null means this race yields no leather at all (most Humanlike races, in this port).</summary>
        public ThingDef? leatherDef;

        public bool Humanlike => intelligence >= Intelligence.Humanlike;

        public bool ToolUser => intelligence >= Intelligence.ToolUser;

        public bool Animal => intelligence == Intelligence.Animal;

        public bool IsFlesh => isFlesh;

        public virtual IEnumerable<string> ConfigErrors()
        {
            if (body == null) yield return "race has no body.";
            if (baseBodySize <= 0f) yield return "baseBodySize must be positive.";
            if (baseHealthScale <= 0f) yield return "baseHealthScale must be positive.";
            if (foodLevelPercentageWantEat <= 0f || foodLevelPercentageWantEat > 1f) yield return "foodLevelPercentageWantEat must be in (0, 1].";
            if (wildness < 0f || wildness > 1f) yield return "wildness must be in [0, 1].";
        }
    }
}
