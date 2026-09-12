using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Pawns
{
    /// <summary>
    /// How much wildlife a map carries, and which animals it can be (systems: mapgen and pawn generation, and the
    /// food economy that stands on hunting).
    ///
    /// <para/><b>The gap this closes.</b> <see cref="BiomeDef.animalDensity"/> — authored for all fourteen
    /// shipped biomes, from an extreme desert's 0.1 to a tropical rainforest's 1.6 — was read by exactly one
    /// line of the core, and that line (<c>World.Gen.WorldGenStep_Deposits</c>) uses it as a proxy for where
    /// game is thick enough to imply a huntable tile, to decide where to place deposits. Nothing anywhere
    /// turned it into an animal. So <c>AI.WorkGiver_Hunt.PotentialWorkThingsGlobal</c> — a work giver
    /// that is built, wired, gated and tested, whose hunters carry bows — scanned every pawn on every
    /// generated map and yielded nothing, for ever. This is the number that was already in content waiting to
    /// answer that, and this class is what finally reads it as what it says.
    ///
    /// <para/>It is the third instance of one defect shape, and the second one <c>BiomeDef</c> was already
    /// carrying the answer to: <c>Building.WildFoodTuning</c> says the same thing about
    /// <see cref="BiomeDef.forageability"/>, and <c>Building.WildPlantSpawner</c> about
    /// <see cref="BiomeDef.wildPlantRegrowDays"/>.
    ///
    /// <para/><b>Two readers, one formula</b> — the discipline <c>Building.WildFoodTuning</c> imposes
    /// on the undergrowth, applied to fauna. <c>MapGen.GenStep_Animals</c> places the standing population a
    /// new map is born with, and <see cref="WildAnimalSpawner"/> restocks it toward the same figure. A map
    /// that restocked toward a second, separately-tuned population would drift away from its own tile over a
    /// game's length.
    ///
    /// <para/><b>A weight budget, not a head count</b> (RimWorld: <c>WildAnimalSpawner.DesiredTotalAnimalWeight</c>,
    /// which sums <c>PawnKindDef.ecoSystemWeight</c> rather than counting animals). This port's
    /// <see cref="PawnKindDef"/> carries no such field, so the nearest thing content does ship stands in for
    /// it: the race's own <see cref="RaceProperties.baseBodySize"/>. Land that supports one muffalo supports
    /// five chickens, which is the shape the field is for; that the scale factor is body size rather than
    /// RimWorld's separately-authored ecosystem weight is this port's substitution, recorded here.
    ///
    /// <para/><b>What the settlement actually gets out of it, and why it is deliberately not much.</b> A
    /// default-sized map in the richest biome this port ships carries a weight budget of ten — call it a
    /// dozen animals — and every one of them butchers to <c>Crafting.HusbandryTuning.MeatPerBodySize</c>
    /// units of <c>Meat_Generic</c> at 0.05 nutrition each. A muffalo is therefore under two nutrition, and
    /// twenty-five citizens burn forty a day. <b>Hunting is a fourth source of food, not a replacement for
    /// the other three</b> (foraging, farming and cooking all landed before it): it is a supplement that
    /// arrives in lumps, and a settlement that tried to live on it would starve. That is the same thing
    /// <c>Building.WildFoodTuning</c> says about forage and it is deliberate in both places — a
    /// wilderness that could feed a town for ever makes a town's own fields decoration.
    /// </summary>
    public static class WildAnimalTuning
    {
        /// <summary>
        /// Map cells per unit of animal weight on land of <see cref="BiomeDef.animalDensity"/> 1
        /// (RimWorld: <c>WildAnimalSpawner.DesiredTotalAnimalWeight</c> is
        /// <c>DesiredAnimalDensity * map.Area / 10000</c> — the shape is recalled, the constant is not
        /// sourceable in this sandbox, so it is <b>this port's own</b> and is pinned by trend tests, never
        /// by the literal). On the default 250×250 map that is a budget of 6.25 at density 1: about seven
        /// animals in a boreal forest, ten in a tropical rainforest, under two in a desert.
        /// </summary>
        public const float CellsPerAnimalWeightAtDensityOne = 10000f;

        /// <summary>
        /// Days a map emptied of wildlife takes to come back to what its biome supports — the fauna
        /// counterpart of <see cref="BiomeDef.wildPlantRegrowDays"/>, which content authors per biome and
        /// this port has no per-biome equivalent of for animals. <b>This port's own number</b>, one in-game
        /// twelfth (<see cref="GenDate.DaysPerTwelfth"/>).
        ///
        /// <para/><b>Chosen against a measurement rather than picked.</b> The first calibration here was a
        /// quadrum, and it failed the one requirement this class exists for: a founded settlement of
        /// twenty-five removed all eight of its map's animals within six days (tamed or killed — see
        /// <see cref="WildAnimalSpawner"/>'s doc on what actually consumes them today), and a map that
        /// empties in six days and refills in fifteen is an empty map with a slow leak, which is the
        /// one-shot-scatter defect wearing a spawner's clothes. At a twelfth, a default-sized temperate map
        /// restocks about one animal a day, which is the same order as what a settlement of that size takes
        /// off it — so the wilderness holds a small standing population instead of collapsing to nothing.
        ///
        /// <para/>It is still nowhere near a food supply: one animal a day at
        /// <c>Crafting.HusbandryTuning.MeatPerBodySize</c> is under two nutrition against the forty a
        /// twenty-five-citizen settlement burns. Pinned by the trends (a cleared map refills within its own
        /// window; a denser biome refills toward more), never by the literal.
        /// </summary>
        public const float RepopulateDays = GenDate.DaysPerTwelfth;

        /// <summary>Self-gate cadence for <see cref="WildAnimalSpawner"/>, the same rare bucket every other
        /// map-scale initiative here uses (<c>Building.FarmingTuning.IntervalTicks</c>). Unlike
        /// <c>Building.WildPlantSpawner</c> — which can ask <c>ListerThings</c> for a def's count in
        /// O(1) and so affords a per-tick roll — measuring the standing wild population means walking the
        /// map's pawns, so this one pays that walk once every <see cref="GenTicks.TickRareInterval"/> ticks
        /// instead of every tick. Wildlife is not urgent on any one tick.</summary>
        public const int IntervalTicks = GenTicks.TickRareInterval;

        /// <summary>
        /// The weight one animal of <paramref name="kind"/> occupies in a map's budget — its race's adult
        /// <see cref="RaceProperties.baseBodySize"/>, deliberately <i>not</i> the live
        /// <see cref="Pawn.BodySize"/> of a particular individual: a map whose animals happened to be
        /// generated young would otherwise read as under-stocked and spawn more, and those would grow up.
        /// The budget is about what the land supports, which does not change while a calf grows.
        /// </summary>
        public static float AnimalWeightOf(PawnKindDef kind)
        {
            if (kind == null) throw new ArgumentNullException(nameof(kind));
            float size = kind.race?.race?.baseBodySize ?? 0f;
            return size > 0f ? size : 1f;
        }

        /// <summary>The same weight for an animal already standing on a map. Falls back to the live
        /// <see cref="Pawn.BodySize"/> for a pawn built by hand with no <see cref="Pawn.kindDef"/> — most of
        /// this suite's animals — so counting never silently reads zero.</summary>
        public static float AnimalWeightOf(Pawn animal)
        {
            if (animal == null) throw new ArgumentNullException(nameof(animal));
            if (animal.kindDef != null) return AnimalWeightOf(animal.kindDef);
            return animal.BodySize > 0f ? animal.BodySize : 1f;
        }

        /// <summary>How much animal weight a map of <paramref name="numGridCells"/> cells on
        /// <paramref name="tile"/> should carry. Generation places this much;
        /// <see cref="WildAnimalSpawner"/> keeps it there. Zero where nothing lives — the ocean and the lake,
        /// whose density is authored at 0 and 0.2 but which no settlement can be founded on anyway.</summary>
        public static float DesiredAnimalWeight(int numGridCells, Tile? tile)
        {
            float density = tile?.biome?.animalDensity ?? 0f;
            if (density <= 0f || numGridCells <= 0) return 0f;
            return numGridCells / CellsPerAnimalWeightAtDensityOne * density;
        }

        /// <summary>
        /// The animal kinds a map may be stocked with, ordered by <see cref="Def.defName"/> so the choice is
        /// reproducible whatever order content loaded in.
        ///
        /// <para/><b>Every animal kind that ships, because content offers nothing finer — stated as a gap,
        /// not as a design.</b> RimWorld chooses per biome from <c>BiomeDef.wildAnimals</c>, a list of
        /// (kind, commonality) records. <see cref="BiomeDef"/> here carries no such list — its own class doc
        /// says so in as many words ("Plant/animal spawn tables belong to later systems") — and the three
        /// animal <see cref="PawnKindDef"/>s this port ships (Husky, Muffalo, Chicken) carry no biome
        /// affinity either, in either direction. <b>So biome-appropriate species selection is not possible
        /// from the content that ships, and this does not pretend otherwise:</b> a tundra and a tropical
        /// swamp draw from the same three kinds, and differ only in how many they hold, which is the one
        /// thing content does say (<see cref="BiomeDef.animalDensity"/>). Authoring a menagerie to hide that
        /// would be inventing content to make this mechanism look finished. The fix is
        /// <c>BiomeDef.wildAnimals</c> as RimWorld has it, and it is fauna content's to add — the day it
        /// exists, this method reads it and nothing else here changes.
        /// </summary>
        public static List<PawnKindDef> WildAnimalKinds()
        {
            var kinds = new List<PawnKindDef>();
            foreach (PawnKindDef kind in DefDatabase<PawnKindDef>.AllDefsListForReading)
            {
                if (kind.race?.race?.Animal == true) kinds.Add(kind);
            }
            kinds.Sort((a, b) => string.CompareOrdinal(a.defName, b.defName));
            return kinds;
        }
    }
}
