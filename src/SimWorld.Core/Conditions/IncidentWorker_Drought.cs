using System;
using System.Globalization;

using SimWorld.Director;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Conditions
{
    /// <summary>
    /// Starts a drought (RimWorld: nothing to port — see <see cref="GameCondition_Drought"/> for why this is a
    /// translation rather than a port).
    ///
    /// <para/><b>Why its own worker, and not a fourth def routed through <see cref="IncidentWorker_MakeGameCondition"/>.</b>
    /// That worker is the right home for <c>HeatWave</c>, <c>ColdSnap</c> and <c>Flashstorm</c>, and wrong for
    /// this one: it rolls duration off the <b>ambient</b> stream (<c>Rand.Range(conditionDef.durationDays)</c>)
    /// and never consults <see cref="Ablation"/> at all. That is fine for three conditions nobody measures yet
    /// and wrong for the one this port wants to attribute a number to — an ambient draw would shift every
    /// later roll in the game the moment this incident became measurable, and with no ablation check there is
    /// no disabled arm to compare against in the first place.
    ///
    /// <para/><b>Its dice are its own.</b> Every roll comes from <see cref="NamedRand"/>, never the ambient
    /// stream, and both figures this incident needs — how severe, how long — are composed <i>before</i> the
    /// <see cref="Ablation"/> check, so a run with the drought switched off and one with it on draw identically
    /// and diverge only in what they do with the answer. This is the discipline
    /// <see cref="Director.IncidentWorker_ManhunterPack"/> established; see that class's own doc for why it is
    /// not optional.
    ///
    /// <para/><b>Where the effect lands is nobody's decision, on purpose.</b> Unlike a raid or a manhunter
    /// pack, this registers at civilization scope and touches every map and every settlement the world's
    /// <see cref="GameConditionManager"/> reaches — <see cref="Building.Plant.TickLong"/> for a watched one,
    /// <see cref="Economy.SettlementSubsistence"/>'s production pass for an unwatched one — so there is no
    /// "where it lands" question for this incident the way there is for a threat aimed at one settlement.
    /// </summary>
    public sealed class IncidentWorker_Drought : IncidentWorker
    {
        /// <summary>The stream every roll in this incident comes from, qualified by the tick so two firings in
        /// one game differ while a replay of the same game does not — the same qualification
        /// <see cref="Director.IncidentWorker_ManhunterPack"/>'s own stream name uses, for the identical
        /// reasoning.</summary>
        private const string StreamName = "Drought";

        /// <summary>
        /// The growth multiplier a firing applies, in (0,1) — never 0 or 1: a drought that did nothing would
        /// not be a drought, and one that ended growth outright is a famine this port does not model. RimWorld
        /// has no drought condition to source this range from; unsourced per CLAUDE.md, so the tests pin that
        /// it actually cuts production by a real amount rather than pinning these literals.
        /// </summary>
        public static readonly FloatRange SeverityRange = new FloatRange(0.25f, 0.7f);

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            GameConditionManager? scope = Find.World?.gameConditionManager;
            return scope != null && !scope.ConditionIsActive(DroughtGameConditionDefOf.Drought);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));

            RandomStream rand = NamedRand.For(
                StreamName + "|" + (Find.TickManager?.TicksGame ?? 0).ToString(CultureInfo.InvariantCulture));

            GameConditionDef conditionDef = DroughtGameConditionDefOf.Drought;

            // Composed before anything below is asked to act: the storyteller has already picked this
            // incident and spent its refire timer, so both arms of one seed draw the same severity and the
            // same duration and differ only in what happens next. See Ablation.
            float severity = rand.Range(SeverityRange);
            float durationDays = rand.Range(conditionDef.durationDays);

            GameConditionManager? scope = Find.World?.gameConditionManager;
            if (scope == null) return false;

            // Ablated: the roll above happened anyway, so the stream lands where it would have. Nothing below
            // this point runs, which is the whole of what an ablated drought is — selected, fired, refire
            // timer spent, and no condition registered, so AggregateGrowthFactor never leaves 1 and
            // ResourceImpactLedger is never written to. See Ablation and ResourceImpactLedger.
            if (Ablation.IsDisabled(def?.defName))
            {
                return true;
            }

            // Defensive, exactly as IncidentWorker_MakeGameCondition.TryExecuteWorker's own re-check is: the
            // storyteller's own CanFireNowSub already refuses to stack a second drought, but TryExecute can be
            // (and is, in tests) called without going through it.
            if (scope.ConditionIsActive(conditionDef)) return false;

            var condition = (GameCondition_Drought)GameConditionMaker.MakeConditionForDays(conditionDef, durationDays);
            condition.Severity = severity;
            scope.RegisterCondition(condition);

            SendLetter(severity, durationDays);
            return true;
        }

        /// <summary>The player is told either way — civilization scope means there is no "watched" and
        /// "unwatched" version of this letter the way a settlement-aimed threat has two.</summary>
        private static void SendLetter(float severity, float durationDays)
        {
            string text = string.Format(
                CultureInfo.InvariantCulture,
                "A drought settles over the civilization. Growth and harvests are down to about {0:0}% of "
                    + "normal, watched or not, and it looks set to last around {1:0.#} days.",
                severity * 100f,
                durationDays);

            Find.LetterStack?.ReceiveLetter("Drought", text, LetterDefOf.NegativeEvent);
        }
    }
}
