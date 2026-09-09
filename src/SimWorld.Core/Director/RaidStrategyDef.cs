using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Factions;

namespace SimWorld.Director
{
    /// <summary>
    /// A raid's tactic (RimWorld: <c>RimWorld.RaidStrategyDef</c>): which <see cref="PawnGroupKindDef"/> to
    /// generate the squad as, whether a faction's tech level even permits this tactic
    /// (<see cref="RaidStrategyWorker.CanUseWith"/>), and how many of <see cref="IncidentParms.points"/> the
    /// squad generator gets to spend (<see cref="pointsFactor"/>).
    /// <para/>
    /// <b>Where this stops:</b> RimWorld's raid strategies also drive a <c>Lord</c> — the squad actually
    /// walking to the colony and fighting a particular way (all-out assault, siege-and-bombard, wait-then-
    /// strike). That needs the AI and Map systems, which this module does not own; see
    /// <see cref="IncidentWorker_RaidEnemy"/>'s class doc for exactly how far generation goes without them.
    /// This def's job, for now, is picking *which* tactic and *how many points* it buys — a real, tested
    /// mechanic — not executing it.
    /// </summary>
    public class RaidStrategyDef : Def
    {
        public Type workerClass = typeof(RaidStrategyWorker_ImmediateAttack);

        /// <summary>
        /// Multiplies <see cref="IncidentParms.points"/> before the squad generator spends them. RimWorld
        /// gives its raid strategies different points-to-danger ratios (a tactic that trades raw numbers for
        /// an edge, like breaching straight through a wall, is worth more danger per point spent than a
        /// straight assault). <b>Not sourced:</b> RimWorld's exact per-strategy multipliers could not be
        /// pinned down, so the values in content are SimWorld's own; the test for this asserts the trend a
        /// factor is supposed to produce (a higher factor buys a materially bigger squad for the same raw
        /// points) rather than a literal number.
        /// </summary>
        public float pointsFactor = 1f;

        /// <summary>Lowest faction tech level this tactic is available to (RimWorld gates Sappers/Siege on a
        /// faction capable of building turrets/mortars; this is SimWorld's translation of that gate onto the
        /// tech ladder generally, not sourced to a specific RimWorld constant).</summary>
        public TechLevel minTechLevel = TechLevel.Undefined;

        /// <summary>Relative pick weight among the tactics a faction's tech level allows (see <see cref="RaidStrategyWorker.CanUseWith"/>).</summary>
        public float selectionWeight = 1f;

        private RaidStrategyWorker? workerInt;

        public RaidStrategyWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (RaidStrategyWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            workerInt = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (workerClass == null || !typeof(RaidStrategyWorker).IsAssignableFrom(workerClass)) yield return "workerClass must derive from RaidStrategyWorker.";
            if (pointsFactor <= 0f) yield return "pointsFactor must be positive.";
            if (selectionWeight <= 0f) yield return "selectionWeight must be positive.";
        }
    }

    /// <summary>
    /// Behaviour behind one <see cref="RaidStrategyDef"/> — which <see cref="PawnGroupKindDef"/> it generates
    /// as, and whether a faction's tech level allows it at all (RimWorld: <c>RimWorld.RaidStrategyWorker</c>,
    /// trimmed to tactic *selection*; see the def's own class doc for what execution this does not cover).
    /// </summary>
    public abstract class RaidStrategyWorker
    {
        public RaidStrategyDef def = null!;

        public virtual PawnGroupKindDef PawnGroupKind => PawnGroupKindDefOf.Combat;

        /// <summary>True when <paramref name="faction"/>'s own tech level meets this tactic's floor.</summary>
        public virtual bool CanUseWith(Faction faction)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            return faction.def.techLevel >= def.minTechLevel;
        }
    }

    /// <summary>Straight assault: the whole squad heads for the colony at once (RimWorld: <c>RimWorld.RaidStrategyWorker_ImmediateAttack</c>). No tech floor — every raiding faction can do this.</summary>
    public sealed class RaidStrategyWorker_ImmediateAttack : RaidStrategyWorker
    {
    }

    /// <summary>Digs in and bombards from range instead of closing to melee (RimWorld: <c>RimWorld.RaidStrategyWorker_Siege</c>, which needs mortars/shells). Gated through content on <see cref="RaidStrategyDef.minTechLevel"/> rather than a hard-coded equipment check.</summary>
    public sealed class RaidStrategyWorker_Siege : RaidStrategyWorker
    {
    }
}
