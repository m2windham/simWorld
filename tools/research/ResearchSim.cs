using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Work;

namespace SimWorld.ReachHarness
{
    /// <summary>What one simulated playthrough produced.</summary>
    public sealed class RunResult
    {
        public string Policy = "";
        public string Modifiers = "";
        public int Seed;

        /// <summary>Projects still part of the tree under this modifier stack (232 unless trimmed).</summary>
        public int ActiveCount;

        /// <summary>Projects handed over free by the scenario's starting era.</summary>
        public int GrantedCount;

        public HashSet<ResearchProjectDef> Finished = new();

        /// <summary>First day each era became complete counting unbroken from era 0; -1 when it never did.</summary>
        public int[] EraTurnDay = Array.Empty<int>();

        /// <summary>
        /// What had been finished at the moment each era turned — the snapshot the "moments per era" framing
        /// needs. Years are not the currency (the horizon decision), so the question is not "how much by year
        /// N" but "how much of an era had this civilization seen by the time it left that era behind".
        /// Null for an era never reached.
        /// </summary>
        public HashSet<ResearchProjectDef>?[] FinishedAtEraTurn = Array.Empty<HashSet<ResearchProjectDef>?>();

        /// <summary>Highest era order reached by unbroken completion; -1 when era 0 never completed.</summary>
        public int MaxEraOrder = -1;

        /// <summary>First day with points to spend and nothing left to buy; -1 when it never happened.</summary>
        public int FirstDryDay = -1;

        public float PointsSpent;

        /// <summary>Points generated on dry days, with nothing to spend them on.</summary>
        public float PointsWasted;

        public float FinalSkill;
    }

    /// <summary>
    /// One playthrough: the real <see cref="ResearchManager"/>, the real
    /// <see cref="ResearchProjectDef.CanStartNow"/> gate and the real <see cref="ResearchProjectDef.CostFactor"/>
    /// curve, driven day by day by a scripted policy against a modelled research economy.
    ///
    /// The economy is the modelled part, and it is modelled because SimWorld ships no ResearchSpeed StatDef
    /// and no work-scheduling loop yet:
    /// points/day = researchers x WorkTicksPerDay x ResearchManager.ResearchPointsPerWorkTick x speed(skill).
    /// speed(skill) reproduces RimWorld's SkillNeed_BaseBonus shape for work-speed stats
    /// (<see cref="SkillSpeedBase"/> + <see cref="SkillSpeedPerLevel"/> x level, so level 8 works at 1.0x).
    /// Those two constants and <see cref="ResearchXpPerWorkTick"/> could not be sourced from SimWorld content
    /// — they are harness assumptions, and docs/research/tech-reachability.md sweeps them.
    /// </summary>
    public sealed class ResearchSim
    {
        public const float SkillSpeedBase = 0.08f;
        public const float SkillSpeedPerLevel = 0.115f;
        public const float ResearchXpPerWorkTick = 0.1f;

        /// <summary>How many <see cref="SkillRecord.Interval"/> decay passes a day holds (200-tick cadence over a 60,000-tick day).</summary>
        private const int SkillIntervalsPerDay = GenDate.TicksPerDay / Pawn_SkillTracker.IntervalTicks;

        /// <summary>Chunks the day's XP is learned in, so SkillRecord's daily saturation throttle can bite mid-day.</summary>
        private const int XpChunksPerDay = 10;

        /// <summary>Research speed multiplier for an Intellectual skill level (1.0 at level 8).</summary>
        public static float SpeedForSkill(float level) => SkillSpeedBase + SkillSpeedPerLevel * level;

        public static RunResult Run(TreeModel tree, HashSet<ResearchProjectDef> excluded, EraRule eraRule, RunConfig config, ResearchPolicy policy, int seed)
        {
            var manager = new ResearchManager();
            Find.ResearchManager = manager;
            var rand = new RandomStream(seed);
            var ctx = new PolicyContext(tree, manager, rand, eraRule, excluded);

            var result = new RunResult
            {
                Policy = policy.Name,
                Seed = seed,
                ActiveCount = ctx.Active.Count,
                EraTurnDay = new int[tree.Eras.Count],
                FinishedAtEraTurn = new HashSet<ResearchProjectDef>?[tree.Eras.Count],
            };
            for (int i = 0; i < result.EraTurnDay.Length; i++) result.EraTurnDay[i] = -1;

            ApplyStartingEra(tree, ctx, manager, config, result);

            SkillRecord? skill = config.SkillGrowth ? MakeResearcherSkill(config) : null;
            int skillLevel = config.StartSkill;
            float attention = policy.AttentionOverride >= 0f ? policy.AttentionOverride : config.Attention;
            int erasComplete = CountCompleteEras(tree, ctx, eraRule);
            RecordEraTurns(result, ctx, erasComplete, 0);
            if (config.EraGatesAvailability) ctx.OpenEraOrder = erasComplete;

            int currentDay = 0;

            // Era progress is re-checked the moment a project finishes, not only at end of day, because
            // under an era gate the day's next candidate depends on whether that finish turned the era.
            bool SyncEras()
            {
                int now = CountCompleteEras(tree, ctx, eraRule);
                if (now <= erasComplete) return false;
                RecordEraTurns(result, ctx, now, currentDay);
                erasComplete = now;
                if (config.EraGatesAvailability) ctx.OpenEraOrder = erasComplete;
                return true;
            }

            int horizonDays = config.HorizonDays;
            for (int day = 1; day <= horizonDays; day++)
            {
                currentDay = day;
                // Skills decay whether or not the day was spent researching, so this runs before the gate.
                if (skill != null)
                {
                    skill.xpSinceMidnight = 0f;
                    for (int i = 0; i < SkillIntervalsPerDay; i++) skill.Interval();
                    skillLevel = skill.levelInt;
                }

                if (rand.Value >= attention) continue;

                if (config.TechLevelTracksEra)
                {
                    EraDef? current = manager.CurrentEra;
                    if (current != null) manager.ResearcherTechLevel = current.techLevel;
                }

                if (policy.SwitchChancePerDay > 0f && manager.CurrentProj != null && rand.Chance(policy.SwitchChancePerDay))
                {
                    manager.CurrentProj = null;
                }

                float researchers = Researchers(config, day);
                float points = researchers * config.WorkTicksPerDay * ResearchManager.ResearchPointsPerWorkTick
                               * SpeedForSkill(skillLevel) * config.ThroughputMultiplier;

                Spend(ctx, manager, policy, config, points, day, result, SyncEras);

                if (skill != null)
                {
                    // XP is per researcher, not per researcher-equivalent-hour: the level is one researcher's,
                    // and SkillRecord.Learn applies passion, the global learning factor and the daily throttle.
                    float chunk = config.WorkTicksPerDay * ResearchXpPerWorkTick / XpChunksPerDay;
                    for (int i = 0; i < XpChunksPerDay; i++) skill.Learn(chunk);
                    skillLevel = skill.levelInt;
                }
            }

            result.FinalSkill = skillLevel;
            result.MaxEraOrder = erasComplete - 1;
            foreach (ResearchProjectDef p in ctx.Active)
            {
                if (manager.IsFinished(p)) result.Finished.Add(p);
            }
            return result;
        }

        /// <summary>
        /// One stand-in researcher whose Intellectual skill runs on the shipped <see cref="SkillRecord"/>:
        /// the real XP curve, the real passion factor and the real level-10+ decay table. The pawn exists
        /// only to own that record — nothing else about it is simulated.
        /// </summary>
        private static SkillRecord MakeResearcherSkill(RunConfig config)
        {
            var pawn = new Pawn(DefDatabase<ThingDef>.GetNamed("Human"), "Researcher");
            SkillRecord record = pawn.skills.GetSkill(DefDatabase<SkillDef>.GetNamed("Intellectual"))!;
            record.Level = config.StartSkill;
            record.passion = config.Passion;
            return record;
        }

        /// <summary>
        /// What ScenPart_StartingEra does at game start: finish every project of every earlier era and set
        /// the researcher tech level to the era's band. Replicated rather than called because the ScenPart
        /// needs an IScenarioContext this harness has no use for.
        /// </summary>
        private static void ApplyStartingEra(TreeModel tree, PolicyContext ctx, ResearchManager manager, RunConfig config, RunResult result)
        {
            EraDef start = DefDatabase<EraDef>.GetNamed(config.StartEra);
            foreach (EraDef era in tree.Eras)
            {
                if (era.order >= start.order) continue;
                foreach (ResearchProjectDef p in tree.InEra(era))
                {
                    if (!ctx.IsActive(p)) continue;
                    manager.FinishProject(p);
                    result.GrantedCount++;
                }
            }
            manager.ResearcherTechLevel = start.techLevel;
        }

        private static float Researchers(RunConfig config, int day)
        {
            if (config.ResearcherGrowthPerYear <= 0f) return config.Researchers;
            float years = day / (float)GenDate.DaysPerYear;
            return MathF.Min(config.MaxResearchers, config.Researchers * MathF.Pow(1f + config.ResearcherGrowthPerYear, years));
        }

        /// <summary>
        /// Spends the day's points, re-picking whenever the current project finishes. <paramref name="syncEras"/>
        /// is called after every completion so an era gate opens on the same day the era turns; a day is only
        /// counted dry once that has been given its chance.
        /// </summary>
        private static void Spend(PolicyContext ctx, ResearchManager manager, ResearchPolicy policy, RunConfig config, float points, int day, RunResult result, Func<bool> syncEras)
        {
            for (int guard = 0; guard < 1024 && points > 0.01f; guard++)
            {
                ResearchProjectDef? current = manager.CurrentProj;
                if (current == null || manager.IsFinished(current))
                {
                    ctx.RefreshCandidates();
                    current = policy.Choose(ctx, config.ChoiceJitter);
                    if (current == null && syncEras())
                    {
                        ctx.RefreshCandidates();
                        current = policy.Choose(ctx, config.ChoiceJitter);
                    }
                    if (current == null)
                    {
                        if (result.FirstDryDay < 0) result.FirstDryDay = day;
                        result.PointsWasted += points;
                        return;
                    }
                    manager.CurrentProj = current;
                }

                float factor = current.CostFactor(manager.ResearcherTechLevel);
                float remaining = (current.baseCost - manager.GetProgress(current)) * factor;
                float apply = MathF.Min(points, remaining + 0.01f);
                manager.ResearchPerformed(apply, null);
                points -= apply;
                result.PointsSpent += apply;
                if (manager.CurrentProj == null) syncEras(); // ResearchPerformed clears it on completion
            }
        }

        private static int CountCompleteEras(TreeModel tree, PolicyContext ctx, EraRule rule)
        {
            int n = 0;
            foreach (EraDef era in tree.Eras)
            {
                if (!rule.IsComplete(era, ctx)) break;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Records the day each newly-complete era turned, and snapshots what was finished at that moment.
        /// The snapshot has to be taken from the <paramref name="ctx"/>'s live manager: <c>RunResult.Finished</c>
        /// is only assembled after the whole run, so reading it here would snapshot an empty set every time —
        /// which is exactly the silent, plausible-looking zero this measurement produced before.
        /// </summary>
        private static void RecordEraTurns(RunResult result, PolicyContext ctx, int erasComplete, int day)
        {
            for (int i = 0; i < erasComplete && i < result.EraTurnDay.Length; i++)
            {
                if (result.EraTurnDay[i] >= 0) continue;
                result.EraTurnDay[i] = day;
                var snapshot = new HashSet<ResearchProjectDef>();
                foreach (ResearchProjectDef p in ctx.Active)
                {
                    if (ctx.Manager.IsFinished(p)) snapshot.Add(p);
                }
                result.FinishedAtEraTurn[i] = snapshot;
            }
        }
    }
}
