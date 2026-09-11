using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Research
{
    /// <summary>
    /// What the civilization studies next, when nobody has told it (system: research — the agenda half of
    /// "a settlement that cannot research cannot climb the era ladder").
    ///
    /// <para/><b>The gap this closes.</b> Exactly one line in all of <c>src/</c> ever assigned
    /// <see cref="ResearchManager.CurrentProj"/>: <c>God.GodManager.Activate</c>, when an edict carrying a
    /// <see cref="God.EdictDef.researchFocus"/> is issued. Five edicts in shipped content carry one, they are
    /// era-gated, and at most <c>GodTuning.MaxActiveEdicts</c> may be active at a time — against an authored
    /// tree of 245 projects. So a civilization whose god issued no edict researched nothing, forever, and one
    /// that issued every edict it could still only ever studied five named projects. Meanwhile
    /// <see cref="AI.WorkGiver_Research"/> skips outright on a null <see cref="ResearchManager.CurrentProj"/>,
    /// so the whole research work type was idle in a game nobody was micromanaging.
    ///
    /// <para/><b>It never overrides the god.</b> <see cref="EnsureProject"/> only ever fills a null, so an
    /// edict's focus — a decision the player made — stands until it is finished, and this picks up again from
    /// the next project onward. That is the same "reorder, don't replace" line
    /// <c>Building.SettlementConstructionInitiative</c> draws for <c>prioritizedConstruction</c>: the god
    /// steers, the civilization does not stop when the god is not looking.
    ///
    /// <para/><b>Why cheapest-first, and why that is not a made-up policy.</b>
    /// <c>docs/research/tech-reachability.md</c> ran 21 archetype policies over 24 seeds against this exact
    /// tree and measured a policy overlap of 0.977 between them (§3.1) — every engaged policy finishes
    /// substantially the same set — because "cost is the only signal the tree carries" (§5.3): no shipped
    /// project is worth more than another to anything in the game. So the choice of policy is measurably
    /// nearly irrelevant, and the honest one to ship is the cheapest startable project: it is what a people
    /// with no outside knowledge would actually do, it never stalls, and it is deterministic.
    /// <c>defName</c> settles a tie so two runs of the same civilization never diverge on Def load order —
    /// determinism is a feature.
    ///
    /// <para/><b>Nothing left to study is not a dead end.</b> When no authored project can be started the
    /// agenda asks <see cref="ResearchManager.EnsureSomethingToResearch"/> to mint the next endless age
    /// (research.endless) and looks once more, so a civilization that finished the ladder keeps going instead
    /// of silently idling — the day the reachability study puts at 3,074 for a two-researcher civilization.
    ///
    /// <para/><b>No state of its own, and so nothing to Scribe.</b> The agenda is a pure function of what the
    /// <see cref="ResearchManager"/> already saves; <see cref="ResearchManager.ExposeData"/> round-trips
    /// <c>currentProj</c> itself, so a loaded game resumes the project it was on rather than re-deciding.
    /// </summary>
    public static class ResearchAgenda
    {
        /// <summary>
        /// Sets <see cref="ResearchManager.CurrentProj"/> when it is null and something can be started.
        /// Returns the project now current (which may be one the god chose, untouched), or null when there is
        /// genuinely nothing startable even after the endless tail has been extended.
        /// </summary>
        public static ResearchProjectDef? EnsureProject()
        {
            ResearchManager manager = Find.ResearchManager;
            if (manager.CurrentProj != null) return manager.CurrentProj;

            ResearchProjectDef? next = NextProject();
            if (next == null)
            {
                // Nothing on the authored ladder can be started. Mint the next endless age and look again,
                // exactly once: EnsureSomethingToResearch is itself a bounded catch-up loop, so a second
                // failure here means the content genuinely has no workable track and looping would spin.
                manager.EnsureSomethingToResearch();
                next = NextProject();
            }

            manager.CurrentProj = next;
            return next;
        }

        /// <summary>
        /// The cheapest project the civilization could start right now (<see cref="ResearchProjectDef.CanStartNow"/>
        /// — not finished, every visible and hidden prerequisite finished), ties broken by <c>defName</c>.
        /// Null when nothing at all is startable. Reads the database live and caches nothing: projects are
        /// minted into it while a game runs (research.endless), so a cache here would go stale the first time
        /// an age opened.
        /// </summary>
        public static ResearchProjectDef? NextProject()
        {
            IReadOnlyList<ResearchProjectDef> all = DefDatabase<ResearchProjectDef>.AllDefsListForReading;
            ResearchProjectDef? best = null;
            for (int i = 0; i < all.Count; i++)
            {
                ResearchProjectDef candidate = all[i];
                if (!candidate.CanStartNow) continue;
                if (best == null || IsCheaperThan(candidate, best)) best = candidate;
            }
            return best;
        }

        private static bool IsCheaperThan(ResearchProjectDef candidate, ResearchProjectDef incumbent)
        {
            if (candidate.baseCost != incumbent.baseCost) return candidate.baseCost < incumbent.baseCost;
            return string.CompareOrdinal(candidate.defName, incumbent.defName) < 0;
        }
    }
}
