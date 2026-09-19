using System;
using System.Collections.Generic;
using SimWorld.Sim;

namespace SimWorld.Conditions
{
    /// <summary>
    /// Every condition in force at one scope (RimWorld: <c>RimWorld.GameConditionManager</c>). There are two
    /// kinds, exactly as RimWorld has two: the world's, which holds conditions over the whole civilization,
    /// and one per <see cref="Map.Map"/>, whose <see cref="Parent"/> is the world's.
    ///
    /// <para/><b>Where a condition lives in this port, and why.</b> The weather module put weather on the map
    /// and argued the god layer should not carry it: weather only moves things that exist on a generated map,
    /// and most settlements are never opened. A condition is the other case, and the reason is the storyteller,
    /// not the thermometer. The only <c>IIncidentTarget</c> this game has is
    /// <c>Director.CivilizationTarget</c> — one target for the whole civilization — and the settlement it
    /// picks for an incident usually has no interior map at all
    /// (<c>World.Settlement.EnterMap</c> generates one lazily, on being looked at). A condition that could
    /// only be registered on a map would therefore exist only while somebody happened to be watching: the
    /// incident would fire, find no map, and change nothing. That is precisely the bug shape this module was
    /// written to remove — it is the same one <c>Director.SettlementRaidResolver</c> was written for
    /// (<c>director.raids.unwatched</c>) — so the civilization-scale manager is not an optional extra here,
    /// it is the default registration site (<see cref="IncidentWorker_MakeGameCondition"/>).
    ///
    /// <para/>The per-map manager is still ported and still real: it is where a condition local to one
    /// settlement's interior belongs, and it is the object a map reads its aggregate through. Nothing in the
    /// shipped content registers at map scope yet; the seam exists so the first thing that needs it does not
    /// have to invent it.
    ///
    /// <para/><b>Cost.</b> Per tick: one pass over this manager's own conditions (normally zero or one), and,
    /// on a map, one walk of a two-deep parent chain. <see cref="AggregateTemperatureOffset"/> is the same
    /// walk and is called once per map per tick by the weather module.
    /// </summary>
    public sealed class GameConditionManager : IExposable
    {
        private readonly Map.Map? ownerMap;

        private List<GameCondition> activeConditions = new List<GameCondition>();

        /// <summary>The civilization-scale manager (RimWorld: <c>Find.World.gameConditionManager</c>).</summary>
        public GameConditionManager()
        {
        }

        /// <summary>One map's own manager; its <see cref="Parent"/> is the world's.</summary>
        public GameConditionManager(Map.Map ownerMap)
        {
            this.ownerMap = ownerMap ?? throw new ArgumentNullException(nameof(ownerMap));
        }

        /// <summary>The map this manager belongs to, or null for the civilization-scale one.</summary>
        public Map.Map? OwnerMap => ownerMap;

        /// <summary>
        /// The manager above this one (RimWorld: <c>GameConditionManager.Parent</c>). Resolved on every read
        /// rather than cached, because a map is routinely constructed before <see cref="Find.World"/> is set
        /// (and, in tests, often without a world at all) — a parent captured at construction time would be
        /// null forever in exactly the case that matters.
        /// </summary>
        public GameConditionManager? Parent => ownerMap == null ? null : Find.World?.gameConditionManager;

        /// <summary>The conditions held at <i>this</i> scope. Conditions inherited from <see cref="Parent"/>
        /// are not in here — see <see cref="ConditionIsActive"/>.</summary>
        public IReadOnlyList<GameCondition> ActiveConditions => activeConditions;

        /// <summary>Starts <paramref name="condition"/> at this scope (RimWorld:
        /// <c>GameConditionManager.RegisterCondition</c>), linking it and calling its
        /// <see cref="GameCondition.Init"/>.</summary>
        public GameCondition RegisterCondition(GameCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            condition.manager = this;
            activeConditions.Add(condition);
            condition.Init();
            return condition;
        }

        /// <summary>Whether a condition of <paramref name="def"/> is in force at this scope or any above it
        /// (RimWorld: <c>GameConditionManager.ConditionIsActive</c>).</summary>
        public bool ConditionIsActive(GameConditionDef def) => GetActiveCondition(def) != null;

        /// <summary>The active condition of <paramref name="def"/>, searching this scope then upward; null
        /// when none is in force.</summary>
        public GameCondition? GetActiveCondition(GameConditionDef def)
        {
            if (def == null) return null;
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++)
                {
                    if (here[i].def == def) return here[i];
                }
            }
            return null;
        }

        /// <summary>
        /// °C every condition reaching this scope adds to the outdoor temperature, this manager's own plus
        /// its ancestors' (RimWorld: <c>GameConditionManager.AggregateTemperatureOffset</c>). Read once per
        /// tick by <c>Weather.WeatherManager</c>, which folds it into the offset it already passed to
        /// <c>Weather.GenTemperature.OutdoorTemperatureAt</c> — so nothing downstream of
        /// <c>Map.outdoorTemperature</c> had to learn that conditions exist.
        /// </summary>
        public float AggregateTemperatureOffset()
        {
            float total = 0f;
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++) total += here[i].TemperatureOffset();
            }
            return total;
        }

        /// <summary>
        /// The multiplier every growth-rate and off-map production figure reaching this scope is scaled by —
        /// this manager's own conditions and its ancestors', multiplied together rather than summed, because 1
        /// rather than 0 is <see cref="GameCondition.GrowthFactor"/>'s identity (RimWorld has no equivalent to
        /// port; the shape mirrors <see cref="AggregateTemperatureOffset"/>). 1 when nothing is active. Read by
        /// <see cref="Building.Plant.TickLong"/> for a watched map and by
        /// <see cref="Economy.SettlementSubsistence"/>'s production pass for an unwatched settlement — the two
        /// places CLAUDE.md's DROUGHT defect scouted, so one number reaches both without either one knowing
        /// what put it there.
        /// </summary>
        public float AggregateGrowthFactor()
        {
            float factor = 1f;
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++) factor *= here[i].GrowthFactor();
            }
            return factor;
        }

        /// <summary>
        /// The defNames of conditions, at this scope or above, currently pulling
        /// <see cref="AggregateGrowthFactor"/> below its identity of 1 — what a caller crediting denied
        /// production (<see cref="Director.ResourceImpactLedger"/>) attributes it to. Read off the condition
        /// itself rather than kept as a literal a caller would have to keep in step with content by hand.
        /// Empty when nothing is suppressing growth, which is the common case and costs one empty walk.
        /// </summary>
        public IEnumerable<string> GrowthFactorSources()
        {
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++)
                {
                    if (here[i].GrowthFactor() < 1f) yield return here[i].def.defName;
                }
            }
        }

        /// <summary>
        /// Splits a growth shortfall between the conditions that caused it and credits
        /// <paramref name="ledger"/> — so that what is attributed sums to what was actually lost, never more.
        ///
        /// <para/><b>Why this is not a loop over the sources crediting each the full amount.</b> That was the
        /// first version and it double-counts the moment two conditions suppress growth at once: the ledger
        /// then reports more nutrition denied than the settlement ever failed to grow. It is the same defect
        /// the death ledger hit one area earlier — a total computed correctly, then handed out without
        /// checking that the parts sum to the whole — and the invariant is the same one: an instrument may
        /// under-claim, never over-claim.
        ///
        /// <para/><b>Why log-share rather than an equal split.</b> Growth factors <i>multiply</i>
        /// (<see cref="AggregateGrowthFactor"/>), so the reduction they jointly cause does not divide evenly:
        /// logarithms turn the product into a sum, which is the one decomposition whose parts add to the whole
        /// by construction. Each condition takes <c>ln(fi) / sum ln(fj)</c> of the shortfall. With factors 0.9
        /// and 0.1 that is roughly 4% and 96%, where an equal split would claim 50/50 and describe neither.
        /// A single source — all today's content can produce — takes all of it, which is exactly why the bug
        /// above is invisible until somebody adds a second growth-suppressing condition.
        /// </summary>
        public void AttributeGrowthShortfall(Director.ResourceImpactLedger? ledger, float nutritionDenied)
        {
            if (ledger == null || nutritionDenied <= 0f) return;

            var suppressors = new List<(string Source, double Factor)>();
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++)
                {
                    float factor = here[i].GrowthFactor();
                    if (factor >= 1f) continue;

                    // A factor of zero has no logarithm; clamped it still dominates every other share, which
                    // is the right answer for a condition that stopped growth outright.
                    suppressors.Add((here[i].def.defName, Math.Max(factor, 1e-6f)));
                }
            }

            if (suppressors.Count == 0) return;
            if (suppressors.Count == 1)
            {
                ledger.RecordNutritionDenied(suppressors[0].Source, nutritionDenied);
                return;
            }

            double totalLog = 0d;
            for (int i = 0; i < suppressors.Count; i++) totalLog += Math.Log(suppressors[i].Factor);
            if (totalLog >= 0d) return;   // unreachable while every factor is below 1; better nothing than a wrong split

            for (int i = 0; i < suppressors.Count; i++)
            {
                double share = Math.Log(suppressors[i].Factor) / totalLog;
                ledger.RecordNutritionDenied(suppressors[i].Source, (float)(nutritionDenied * share));
            }
        }

        /// <summary>
        /// One tick of this scope (RimWorld: <c>GameConditionManager.GameConditionManagerTick</c>): every
        /// condition held here ticks and expires, and then — on a map — every condition reaching this map,
        /// inherited ones included, gets its per-map pass. See <see cref="GameCondition"/> for why the
        /// per-map half is pulled from here rather than pushed from the condition.
        /// <para/>
        /// The world's manager is ticked by <c>World.World.WorldTick</c> (a pre-ticker) and a map's by
        /// <c>Map.Map.MapTick</c> (a post-ticker), so within one game tick a world-scale condition has
        /// already advanced its own clock before any map asks it to act.
        /// </summary>
        public void GameConditionManagerTick()
        {
            for (int i = activeConditions.Count - 1; i >= 0; i--)
            {
                GameCondition condition = activeConditions[i];
                condition.GameConditionTick();
                if (condition.Expired)
                {
                    activeConditions.RemoveAt(i);
                    condition.End();
                }
            }

            if (ownerMap == null) return;
            for (GameConditionManager? m = this; m != null; m = m.Parent)
            {
                List<GameCondition> here = m.activeConditions;
                for (int i = 0; i < here.Count; i++) here[i].GameConditionTickOnMap(ownerMap);
            }
        }

        public void ExposeData()
        {
            List<GameCondition>? list = activeConditions;
            Scribe_Collections.Look(ref list, "activeConditions", LookMode.Deep);
            activeConditions = list ?? new List<GameCondition>();

            // The back-link is derived, not saved: a condition belongs to whichever manager loaded it, and
            // re-deriving it here means a save can never restore a condition pointing at the wrong scope.
            for (int i = 0; i < activeConditions.Count; i++) activeConditions[i].manager = this;
        }
    }
}
