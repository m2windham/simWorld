using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    /// <summary>
    /// A living, growing Thing (RimWorld: <c>Verse.Plant</c>). Growth advances on the long tick
    /// (<c>tickerType</c> is content, set <c>Long</c> on every growable/decorative plant Def) at a rate that
    /// is fertility × light × temperature — RimWorld's own three-factor product — via
    /// <see cref="PlantUtility"/>. <b>Scope:</b> only the growth rate itself is ported; RimWorld's further
    /// mechanics off the back of it (dying from cold/heat, blight, wilting, growth stages/visuals) are not —
    /// see this module's report.
    /// </summary>
    public class Plant : Thing
    {
        /// <summary>Growth a freshly sown plant starts at — enough above zero to read as "just planted"
        /// rather than "nothing here" for anything that inspects it before its first long tick. RimWorld's
        /// real starting value is not sourced here; pinned by a test asserting it is small and positive, not
        /// by this literal.</summary>
        public const float SeedlingGrowth = 0.01f;

        /// <summary>Modest, unsourced xp for a completed harvest — same shape as <see cref="Frame.CompletionXp"/>'s
        /// own comment, scaled down since harvesting one plant is a smaller act than finishing a building.</summary>
        public const float HarvestXp = 30f;

        private float growth;

        /// <summary>0 (freshly sown) to 1 (fully mature); clamped on every write.</summary>
        public float Growth
        {
            get => growth;
            set => growth = GenMath.Clamp01(value);
        }

        public bool FullyGrown => growth >= 1f;

        /// <summary>True once this plant is grown enough to harvest and harvesting it yields something at
        /// all (RimWorld: <c>Plant.HarvestableNow</c>).</summary>
        public bool HarvestableNow => def.plant?.harvestedThingDef != null && growth >= (def.plant?.harvestMinGrowth ?? 1f);

        /// <summary>
        /// Most plants this pass spawns (today: only <c>WildPlant</c>, scattered by MapGen as pre-existing
        /// scrub) start already established rather than freshly sown — <see cref="JobDriver_Sow"/> resets
        /// this to <see cref="SeedlingGrowth"/> explicitly right after spawning a plant it sowed.
        /// </summary>
        public override void PostMake()
        {
            base.PostMake();
            growth = 1f;
        }

        public override void TickLong()
        {
            base.TickLong();
            PlantProperties? props = def.plant;
            Map.Map? map = Map;
            if (props == null || map == null || FullyGrown) return;

            float fertility = map.terrainGrid.TerrainAt(Position).fertility;
            float light = PlantUtility.GrowthRateFactor_Light(Find.TickManager.TicksAbs);
            float temperature = PlantUtility.GrowthRateFactor_Temperature(map.outdoorTemperature);
            float baseRateFactor = fertility * light * temperature;
            if (baseRateFactor <= 0f) return;

            // Standing conditions (a drought, today the only one) scale on top of RimWorld's own three
            // factors above — see GameConditionManager.AggregateGrowthFactor. 1 with nothing active, so an
            // unaffected map pays for one cheap walk of an empty list and nothing else.
            float conditionFactor = map.gameConditionManager.AggregateGrowthFactor();
            float rateFactor = baseRateFactor * conditionFactor;

            float growDays = props.growDays > 0f ? props.growDays : 0.01f;
            float ticksToFullyGrow = GenDate.TicksPerDay * growDays;
            float intervalFraction = GenTicks.TickLongInterval / ticksToFullyGrow;

            if (conditionFactor < 1f) RecordGrowthDenied(map, props, baseRateFactor * intervalFraction, conditionFactor);

            Growth += rateFactor * intervalFraction;
        }

        /// <summary>
        /// Credits <see cref="Director.ResourceImpactLedger"/> with the nutrition this tick's growth fell
        /// short of, in nutrition, because a standing condition scaled it down.
        ///
        /// <para/><b>Exact, and from this one call — never a second simulated tick.</b>
        /// <paramref name="growthWithoutCondition"/> is the growth this same tick would have earned at the
        /// condition's identity factor of 1 (RimWorld's own fertility × light × temperature product, with
        /// nothing else touching it); the gap between that and what was actually applied is
        /// <c>growthWithoutCondition × (1 − conditionFactor)</c>, algebra rather than a control run. Converted
        /// to nutrition by the same two figures <see cref="Harvest"/> converts a harvest to nutrition with —
        /// <see cref="PlantProperties.harvestYield"/> and the harvested def's own
        /// <see cref="Crafting.IngestibleProperties.nutrition"/> — at growth 1, i.e. what one full growth-point of this
        /// plant is worth, ignoring the difficulty's crop-yield factor and the random rounding
        /// <see cref="Harvest"/> applies at collection time: this instrument reads the rate the condition
        /// touched, not the dice a later harvest will separately roll.
        /// </summary>
        private static void RecordGrowthDenied(
            Map.Map map, PlantProperties props, float growthWithoutCondition, float conditionFactor)
        {
            float nutritionPerFullGrowth = props.harvestYield * (props.harvestedThingDef?.ingestible?.nutrition ?? 0f);
            if (nutritionPerFullGrowth <= 0f) return;

            float nutritionDenied = growthWithoutCondition * (1f - conditionFactor) * nutritionPerFullGrowth;
            if (nutritionDenied <= 0f) return;

            map.gameConditionManager.AttributeGrowthShortfall(Find.Storyteller?.resourceImpact, nutritionDenied);
        }

        /// <summary>
        /// Destroys this plant and spawns its harvest yield, scaled by how grown it actually was (RimWorld:
        /// <c>Plant.PlantCollected</c>/harvest handling, folded into one call since no separate "collected vs.
        /// cut" distinction exists here). A no-op yield (no <see cref="PlantProperties.harvestedThingDef"/>,
        /// or a growth-scaled amount that rounds to 0) still destroys the plant — matching a real harvest
        /// clearing the ground either way.
        /// </summary>
        public void Harvest(Pawn? worker)
        {
            PlantProperties? props = def.plant;
            Map.Map? map = Map;
            IntVec3 pos = Position;
            Rot4 rot = Rotation;

            int yield = 0;
            if (props?.harvestedThingDef != null && props.harvestYield > 0)
            {
                // DifficultyDef.cropYieldFactor, the last multiplier before the rounding — RimWorld applies it
                // the same way at the end of Plant.YieldNow, after growth and before the random round, so a
                // difficulty that halves yields halves the *expected* harvest rather than shifting where the
                // rounding lands. (Its exact RimWorld call site is not sourced here; the ordering between two
                // difficulties is what the test pins, not this expression.) Still one draw from the seeded
                // stream either way, so a scaled harvest consumes exactly as much randomness as an unscaled
                // one and no other roll in the tick moves.
                float amount = props.harvestYield * growth * Director.DifficultyUtility.CropYieldFactor;
                yield = GenMath.RoundRandom(amount, Rand.Current);
            }

            Destroy(DestroyMode.Vanish);

            if (map != null && props?.harvestedThingDef != null && yield > 0)
            {
                Thing stack = ThingMaker.MakeThing(props.harvestedThingDef);
                stack.stackCount = yield;
                GenSpawn.Spawn(stack, pos, map, rot);
            }

            worker?.skills?.GetSkill(SkillDefOf.Plants)?.Learn(HarvestXp);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref growth, "growth", 1f);
        }
    }
}
