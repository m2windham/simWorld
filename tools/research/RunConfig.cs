using System.Collections.Generic;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.ReachHarness
{
    /// <summary>
    /// Everything a single simulated playthrough depends on. Every field is an explicit modelling choice;
    /// the ones marked ASSUMPTION are not sourced from shipped content or from RimWorld and are the numbers
    /// docs/research/tech-reachability.md runs a sensitivity sweep over.
    /// </summary>
    public sealed class RunConfig
    {
        /// <summary>In-game years simulated. ASSUMPTION: 50 baseline (Epoch's reachability panel used 45).</summary>
        public int HorizonYears = 50;

        /// <summary>Scenario start era by defName; every earlier era's projects are granted, per ScenPart_StartingEra.</summary>
        public string StartEra = "SticksAndStones";

        /// <summary>Researcher-equivalents at day 0. ASSUMPTION: 2, the RimWorld colony convention the port inherits.</summary>
        public float Researchers = 2f;

        /// <summary>Multiplicative growth in researcher-equivalents per in-game year. 0 = a colony that never grows.</summary>
        public float ResearcherGrowthPerYear;

        /// <summary>Ceiling on grown researcher-equivalents.</summary>
        public float MaxResearchers = 1000f;

        /// <summary>Intellectual skill each researcher-equivalent starts at.</summary>
        public int StartSkill = 8;

        /// <summary>
        /// Drive the skill through a real <see cref="SimWorld.Work.SkillRecord"/> — the shipped XP curve,
        /// passion factor, daily learning saturation and level-10+ decay. False pins the skill at
        /// <see cref="StartSkill"/>.
        /// </summary>
        public bool SkillGrowth = true;

        /// <summary>Passion of the researcher-equivalent, feeding SkillRecord.LearnRateFactor (None 0.35, Minor 1.0, Major 1.5).</summary>
        public Passion Passion = Passion.Minor;

        /// <summary>
        /// Ticks per day one researcher-equivalent spends on the research work type. ASSUMPTION: 15,000 of
        /// the 60,000-tick day (GenDate.TicksPerDay) — roughly six waking hours left for one work type
        /// after sleep, meals and recreation.
        /// </summary>
        public float WorkTicksPerDay = 15000f;

        /// <summary>Fraction of days research actually happens at all (a bench without a researcher, an interrupted queue).</summary>
        public float Attention = 0.85f;

        /// <summary>Relative noise on a policy's preference key, so a policy is a plausible player rather than a perfect optimizer.</summary>
        public float ChoiceJitter = 0.15f;

        /// <summary>Multiplier on the day's research points, applied by throughput modifiers.</summary>
        public float ThroughputMultiplier = 1f;

        /// <summary>
        /// When set, ResearchManager.ResearcherTechLevel follows CurrentEra each day. Shipped behaviour is
        /// false: nothing in the runtime advances it after ScenPart_StartingEra sets it once.
        /// </summary>
        public bool TechLevelTracksEra;

        /// <summary>
        /// When set, a project is only researchable once its era is open — era N opens when eras 0..N-1 are
        /// complete under the active <see cref="EraRule"/>. Shipped behaviour is false: ResearchProjectDef.CanStartNow
        /// checks prerequisites only, so an Exotic-era project is legal on day 1 if its chain allows.
        /// </summary>
        public bool EraGatesAvailability;

        public RunConfig Clone() => (RunConfig)MemberwiseClone();

        public int HorizonDays => HorizonYears * GenDate.DaysPerYear;

        /// <summary>The 24 fixed seeds the panel runs, matching Epoch's panel width.</summary>
        public static IReadOnlyList<int> DefaultSeeds { get; } = new[]
        {
            1, 2, 3, 5, 8, 13, 21, 34, 55, 89, 144, 233,
            1000, 1001, 1002, 2718, 3141, 4242, 5150, 6006, 7777, 8675, 9001, 12345,
        };
    }
}
