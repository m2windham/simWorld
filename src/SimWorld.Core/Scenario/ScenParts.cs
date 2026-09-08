using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Scenario
{
    // RimWorld's ScenPart_StatFactor and ScenPart_PermaGameCondition are intentionally not ported: both need
    // systems this port doesn't own yet (a live StatDef pipeline on pawns, and map-wide game conditions).
    // Their ScenPartDefs are omitted from content too, rather than shipping a class with nothing to do.

    /// <summary>How many starting pawns the player has and chooses from (RimWorld: <c>RimWorld.ScenPart_ConfigPage_ConfigureStartingPawns</c>).
    /// Doesn't itself generate pawns — <see cref="StartingPawnCount"/> is read by whichever pawn-generation
    /// step runs before <see cref="Scenario.PostGameStart"/>.</summary>
    public sealed class ScenPart_ConfigPage_ConfigureStartingPawns : ScenPart
    {
        public int pawnCount = 3;
        public int pawnChoiceCount = 3;

        public int StartingPawnCount => pawnCount;

        public override string Summary(Scenario scen) => "You start with " + pawnCount + " pawns.";

        public override IEnumerable<string> ConfigErrors()
        {
            if (pawnCount <= 0) yield return "pawnCount must be > 0.";
            if (pawnChoiceCount < pawnCount) yield return "pawnChoiceCount must be >= pawnCount.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pawnCount, "pawnCount", 3);
            Scribe_Values.Look(ref pawnChoiceCount, "pawnChoiceCount", 3);
        }
    }

    /// <summary>Sets the player's civilization (RimWorld: <c>RimWorld.ScenPart_PlayerFaction</c>).</summary>
    public sealed class ScenPart_PlayerFaction : ScenPart
    {
        public FactionDef factionDef = null!;

        public override string Summary(Scenario scen) => "You play as the " + (factionDef?.LabelCap ?? "?") + ".";

        public override void PostGameStart(IScenarioContext ctx) => ctx.PlayerFactionDef = factionDef;

        public override IEnumerable<string> ConfigErrors()
        {
            if (factionDef == null) yield return "factionDef is required.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            FactionDef? f = factionDef;
            Scribe_Defs.Look(ref f, "factionDef");
            factionDef = f!;
        }
    }

    /// <summary>Gives a fixed starting item (RimWorld: <c>RimWorld.ScenPart_StartingThing_Defined</c>).</summary>
    public sealed class ScenPart_StartingThing_Defined : ScenPart
    {
        public ThingDef thingDef = null!;
        public int count = 1;

        public override string Summary(Scenario scen) => "Start with " + count + " " + (thingDef?.LabelCap ?? "?") + ".";

        public override void PostGameStart(IScenarioContext ctx) => ctx.AddStartingThing(thingDef, count);

        public override IEnumerable<string> ConfigErrors()
        {
            if (thingDef == null) yield return "thingDef is required.";
            if (count <= 0) yield return "count must be > 0.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ThingDef? t = thingDef;
            Scribe_Defs.Look(ref t, "thingDef");
            thingDef = t!;
            Scribe_Values.Look(ref count, "count", 1);
        }
    }

    /// <summary>Finishes one research project outright (RimWorld: <c>RimWorld.ScenPart_StartingResearch</c>-equivalent).</summary>
    public sealed class ScenPart_StartingResearch : ScenPart
    {
        public ResearchProjectDef project = null!;

        public override string Summary(Scenario scen) => "You already know " + (project?.LabelCap ?? "?") + ".";

        public override void PostGameStart(IScenarioContext ctx) => ctx.ResearchManager.FinishProject(project);

        public override IEnumerable<string> ConfigErrors()
        {
            if (project == null) yield return "project is required.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ResearchProjectDef? p = project;
            Scribe_Defs.Look(ref p, "project");
            project = p!;
        }
    }

    /// <summary>Gives every starting pawn a trait, unless they already have it or a conflicting one
    /// (RimWorld: <c>RimWorld.ScenPart_ForcedTrait</c>). <see cref="context"/> matches RimWorld's field for
    /// parity, but <see cref="IScenarioContext.StartingPawns"/> only ever holds player pawns, so it only
    /// gates out <see cref="PawnGenerationContext.NonPlayer"/>.</summary>
    public sealed class ScenPart_ForcedTrait : ScenPart
    {
        public TraitDef trait = null!;
        public int degree;
        public float chance = 1f;
        public PawnGenerationContext context = PawnGenerationContext.PlayerStarter;

        public override string Summary(Scenario scen) => "Starting pawns may have the " + (trait?.LabelCap ?? "?") + " trait.";

        public override void PostGameStart(IScenarioContext ctx)
        {
            if (context == PawnGenerationContext.NonPlayer) return;
            foreach (Pawn pawn in ctx.StartingPawns)
            {
                if (!Rand.Chance(chance)) continue;
                if (pawn.story.traits.HasTrait(trait)) continue;
                if (ConflictsWithExisting(pawn)) continue;
                pawn.story.traits.GainTrait(new Trait(trait, degree, forced: true));
            }
        }

        private bool ConflictsWithExisting(Pawn pawn)
        {
            foreach (Trait existing in pawn.story.traits.allTraits)
            {
                if (existing.def.ConflictsWith(trait)) return true;
            }
            return false;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (trait == null) yield return "trait is required.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            TraitDef? t = trait;
            Scribe_Defs.Look(ref t, "trait");
            trait = t!;
            Scribe_Values.Look(ref degree, "degree");
            Scribe_Values.Look(ref chance, "chance", 1f);
            Scribe_Values.Look(ref context, "context", PawnGenerationContext.PlayerStarter);
        }
    }

    /// <summary>Sends an introductory letter when the game starts (RimWorld: <c>RimWorld.ScenPart_GameStartDialog</c>).</summary>
    public sealed class ScenPart_GameStartDialog : ScenPart
    {
        public string text = "";

        public override void PostGameStart(IScenarioContext ctx) =>
            ctx.LetterStack.ReceiveLetter(def.label ?? "Game start", text, Letters.LetterDefOf.NeutralEvent);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref text, "text", "");
        }
    }

    /// <summary>
    /// SimWorld translation part: finishes every research project of every era earlier than <see cref="era"/>
    /// and sets <see cref="ResearchManager.ResearcherTechLevel"/> to that era's tech level — the scenario-time
    /// equivalent of picking a starting point on the era ladder (see <see cref="Research.EraDef"/>).
    /// </summary>
    public sealed class ScenPart_StartingEra : ScenPart
    {
        public EraDef era = null!;

        public override string Summary(Scenario scen) => "You start in the " + (era?.LabelCap ?? "?") + " era.";

        public override void PostGameStart(IScenarioContext ctx)
        {
            foreach (EraDef other in DefDatabase<EraDef>.AllDefsListForReading)
            {
                if (other.order >= era.order) continue;
                // Seeded, not narrated: see ResearchManager.SetProjectFinishedForSetup for why a scenario's
                // starting era must not announce the eras it hands the civilization for free.
                foreach (ResearchProjectDef project in other.Projects) ctx.ResearchManager.SetProjectFinishedForSetup(project);
            }
            ctx.ResearchManager.ResearcherTechLevel = era.techLevel;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            if (era == null) yield return "era is required.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            EraDef? e = era;
            Scribe_Defs.Look(ref e, "era");
            era = e!;
        }
    }

    /// <summary>
    /// SimWorld translation part: how many rival civilizations world generation should seed. Not read through
    /// <see cref="IScenarioContext"/> — world generation is a separate system and reads
    /// <see cref="Scenario.RivalCivilizationCount"/> via the test-only <see cref="Scenario.Current"/> hook instead.
    /// </summary>
    public sealed class ScenPart_RivalCivilizations : ScenPart
    {
        public IntRange count = new IntRange(3, 6);

        public override string Summary(Scenario scen) => "Rival civilizations: " + count + ".";

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref count, "count");
        }
    }

    /// <summary>
    /// SimWorld translation part: a preferred biome for world generation to place the starting civilization
    /// in. Record-only — no PostWorldGenerate/PostGameStart hook yet; world generation is a separate system.
    /// </summary>
    public sealed class ScenPart_StartingBiome : ScenPart
    {
        public BiomeDef biome = null!;

        public override string Summary(Scenario scen) => "Preferred biome: " + (biome?.LabelCap ?? "?") + ".";

        public override IEnumerable<string> ConfigErrors()
        {
            if (biome == null) yield return "biome is required.";
        }

        public override void ExposeData()
        {
            base.ExposeData();
            BiomeDef? b = biome;
            Scribe_Defs.Look(ref b, "biome");
            biome = b!;
        }
    }
}
