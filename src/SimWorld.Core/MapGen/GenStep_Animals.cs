using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.MapGen
{
    /// <summary>
    /// Puts the wildlife on a new map (RimWorld: <c>RimWorld.GenStep_Animals</c>), as much of it as the
    /// tile's <see cref="BiomeDef.animalDensity"/> supports — the standing population a band arriving here
    /// finds already living on the land.
    ///
    /// <para/><b>Why this step exists at all.</b> Nothing in this codebase had ever spawned a wild animal on
    /// an interior map. The hunting chain above it was complete and unreachable:
    /// <c>AI.WorkGiver_Hunt.PotentialWorkThingsGlobal</c> walks every pawn on the map looking for an
    /// unowned animal and, on every map this port had ever generated, found none — so the work type showed
    /// in every citizen's priority grid and could never produce a job, for ever. See
    /// <see cref="WildAnimalTuning"/> for the content-side half of that story (a biome field authored for
    /// fourteen biomes and read by one line of the core, which uses it to decide where to put deposits).
    ///
    /// <para/><b>A step of its own, where the food plants are not.</b> The wild berry
    /// (<c>Building.WildFoodDefOf.Plant_Berry</c>) is scattered from inside
    /// <see cref="GenStep_Scatterers"/>, because placing it is the same act as placing ordinary undergrowth.
    /// An animal is not: it is a <see cref="Pawn"/> with needs, a mind and a think tree, generated through
    /// <c>Pawns.Generation.PawnGenerator</c>, and it has to go on ground nothing later in the pipeline will
    /// clear. Hence RimWorld's own shape — a separate <c>GenStep</c>, and the last one in the pipeline.
    ///
    /// <para/><b>Last, deliberately.</b> <see cref="GenStep_Ruins"/> clears its footprint and
    /// <see cref="GenStep_Roads"/> clears its path of whatever earlier steps left there. Both run over
    /// things; neither should ever have to decide what to do about a living animal standing in the way. So
    /// the animals arrive after the map has stopped changing shape, and every cell one is placed on is a
    /// cell that is still open when generation ends.
    ///
    /// <para/><b>Determinism.</b> The stocking seed comes from this step's own
    /// <see cref="GenStep.SeededStream"/>, which is derived from the map's seed string — so the same world
    /// tile generated twice is born with the same animals in the same cells, and generating animals moves no
    /// other step's stream. <see cref="WildAnimalSpawner.TrySpawnOne"/> carries that determinism through
    /// pawn generation itself; see its own doc for how.
    /// </summary>
    public class GenStep_Animals : GenStep
    {
        public override void Generate(MapGenContext ctx)
        {
            RandomStream rand = SeededStream(ctx);
            WildAnimalSpawner.StockMap(ctx.map, ctx.tile, rand.Int);
        }
    }
}
