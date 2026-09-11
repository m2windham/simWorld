using System.Collections.Generic;
using System.Linq;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Director
{
    /// <summary>Tunables for <see cref="StorytellerComp_Disease"/> (RimWorld: <c>Verse.StorytellerCompProperties_Disease</c>).</summary>
    public sealed class StorytellerCompProperties_Disease : StorytellerCompProperties
    {
        public IncidentCategoryDef category = null!;

        /// <summary>
        /// The value of <see cref="baseMtbDays"/> a storyteller is neutral at. Not a RimWorld number — it is
        /// this field's own declared default, read as "leave the biome's authored rate alone", so a
        /// storyteller that says nothing about disease changes nothing about it. See
        /// <see cref="StorytellerComp_Disease.MeanTimeBetweenDiseaseDays"/>.
        /// </summary>
        public const float NeutralBaseMtbDays = 10f;

        /// <summary>
        /// How much disease this storyteller wants, as a mean time between diseases in days.
        ///
        /// <para/><b>This used to be the whole answer and is now half of it.</b> RimWorld reads the rate off
        /// the map's biome (<c>BiomeDef.diseaseMtbDays</c>); this field's own doc used to say "SimWorld has
        /// no biomes yet, so it is a flat per-difficulty-scaled value here instead". SimWorld has had biomes
        /// for a long time, and a civilization whose settlements sit on real world tiles can be asked which
        /// ones — so the biome supplies the rate, and this number survives as the storyteller's appetite
        /// relative to <see cref="NeutralBaseMtbDays"/>: Phoebe's 16 stretches every biome's mean time by
        /// 1.6×, Cassandra's 10 leaves it as content authored it. It is still the whole answer for a target
        /// with no settlements behind it (a bare <see cref="CivilizationTarget"/> — a test pose), which is
        /// why it keeps a sensible standalone value rather than becoming a bare multiplier.
        /// </summary>
        public float baseMtbDays = NeutralBaseMtbDays;

        public StorytellerCompProperties_Disease() : base(typeof(StorytellerComp_Disease))
        {
        }
    }

    /// <summary>
    /// Rolls a mean-time-between check for disease every interval, scaled by
    /// <see cref="DifficultyDef.diseaseIntervalFactor"/> and — the part that makes a rainforest different
    /// from a desert — by the biomes the civilization actually lives in
    /// (<see cref="MeanTimeBetweenDiseaseDays"/>).
    /// </summary>
    public sealed class StorytellerComp_Disease : StorytellerComp
    {
        private StorytellerCompProperties_Disease Props => (StorytellerCompProperties_Disease)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            float mtb = MeanTimeBetweenDiseaseDays(target);
            if (!Rand.Current.MTBEventOccurs(mtb, GenDate.TicksPerDay, Storyteller.IncidentCycleLengthTicks)) yield break;

            IncidentParms parms = GenerateParms(Props.category, target);
            List<IncidentDef> usable = UsableIncidentsInCategory(Props.category, parms).ToList();
            if (usable.Count == 0) yield break;
            if (!GenCollection.TryRandomElementByWeight(usable, d => IncidentChanceFinal(d, target), Rand.Current, out IncidentDef picked)) yield break;

            yield return new FiringIncident(picked, this, parms);
        }

        /// <summary>
        /// Mean days between diseases for this target, before the roll. Public and pure so the biome's effect
        /// is testable without leaning on a probabilistic roll.
        ///
        /// <para/><b>A civilization is several places, so its risks add.</b> RimWorld has one
        /// <c>IIncidentTarget</c> per map and one biome per map, and each colony rolls its own check. Here
        /// one target stands for the whole civilization, so the settlements' rates are summed — a town in a
        /// tropical swamp (a short mean time) contributes far more risk than one on the tundra, three towns
        /// catch more disease than one, and the civilization's rate is the rate of *something* happening
        /// *somewhere* in it, which is exactly what one roll against one target can mean. A biome that never
        /// gets sick (<see cref="BiomeDef.diseaseMtbDays"/> left at infinity) contributes no rate rather than
        /// dragging an average upwards, so one arctic outpost cannot make a swamp civilization healthier.
        ///
        /// <para/>Falls back to <see cref="StorytellerCompProperties_Disease.baseMtbDays"/> alone when no
        /// settlement resolves a biome at all: no world is loaded, the target is a hand-posed
        /// <see cref="CivilizationTarget"/>, or the tiles carry no biome. That is the behaviour this comp had
        /// before it consulted the world, kept deliberately so a test that poses a civilization still gets a
        /// disease rate.
        ///
        /// <para/>Draw-count note: one <see cref="RandomStream.MTBEventOccurs"/> roll per interval, exactly as
        /// before — reading the biome costs no randomness. Only the probability that roll is taken against
        /// changes.
        /// </summary>
        public float MeanTimeBetweenDiseaseDays(IIncidentTarget target)
        {
            float difficultyFactor = Find.Storyteller.difficulty?.diseaseIntervalFactor ?? 1f;
            float appetite = Props.baseMtbDays / StorytellerCompProperties_Disease.NeutralBaseMtbDays;

            int biomesRead = 0;
            float ratePerDay = 0f;
            if (target is CivilizationTarget civilization)
            {
                IReadOnlyList<Settlement> settlements = civilization.Settlements;
                for (int i = 0; i < settlements.Count; i++)
                {
                    BiomeDef? biome = BiomeOf(settlements[i]);
                    if (biome == null) continue;
                    biomesRead++;
                    float days = biome.diseaseMtbDays;
                    if (days > 0f && !float.IsPositiveInfinity(days)) ratePerDay += 1f / days;
                }
            }

            if (biomesRead == 0) return Props.baseMtbDays * difficultyFactor;
            if (ratePerDay <= 0f) return float.PositiveInfinity; // every settlement sits in a biome that never sickens anyone.
            return 1f / ratePerDay * appetite * difficultyFactor;
        }

        /// <summary>The biome of the world tile a settlement stands on, or null when no world is current or
        /// the tile is outside its grid (<see cref="Weather.MapClimate.TryResolve"/> resolves a map's tile
        /// the same guarded way, and for the same reason).</summary>
        private static BiomeDef? BiomeOf(Settlement settlement)
        {
            global::SimWorld.World.World? world = Find.World;
            if (world == null || world.grid == null) return null;
            if (settlement.tile < 0 || settlement.tile >= world.grid.TilesCount) return null;
            return world.grid.Tiles[settlement.tile].biome;
        }
    }
}
