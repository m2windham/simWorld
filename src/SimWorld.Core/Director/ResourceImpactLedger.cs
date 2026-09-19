using System;
using System.Collections.Generic;

using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// Nutrition denied to production by a standing condition, keyed by what caused it — the resource-side
    /// analogue of <see cref="DeathLedger"/>, built the same way: a source only appears once it has actually
    /// denied something, so an unaffected game costs this ledger nothing and never lists a defect that never
    /// ran.
    ///
    /// <para/><b>Measured where it acts, not by a control arm.</b> A before/after comparison — run the
    /// simulation, run it again with the defect off, subtract — only holds if switching the defect changes
    /// nothing else, and it pays for every measurement twice over. This ledger is instead credited at the
    /// exact point a defect's effect is computed: <see cref="Building.Plant.TickLong"/> and
    /// <see cref="Economy.SettlementSubsistence"/>'s private production pass both work out, inline and
    /// algebraically, what this one tick or pass would have produced at the standing condition's identity
    /// factor of 1, and credit the difference. No second simulated run, ever, and the number is exact rather
    /// than sampled.
    ///
    /// <para/><b>Why it lives on <see cref="Storyteller"/> and not on the condition that caused it.</b> A
    /// <see cref="Conditions.GameCondition"/> ends — expires, is removed — and the harm it already did must
    /// not end with it: a save taken after a drought has broken must still be able to say how much it cost.
    /// <see cref="DeathLedger"/>'s own doc makes the identical argument about <see cref="Storyteller.Chronicle"/>'s
    /// cap; this is the same argument about a condition's lifetime.
    ///
    /// <para/><b>Why an ablated defect reads exactly zero here, and not by a special case in this class.</b>
    /// An ablated incident (<see cref="Sim.Ablation"/>) registers no condition, so
    /// <see cref="Conditions.GameConditionManager.AggregateGrowthFactor"/> never leaves 1 at any scope, so
    /// neither call site above is ever below its own "nothing to record" guard, so
    /// <see cref="RecordNutritionDenied"/> is never called at all. The self-check is a property of where the
    /// write happens, not something this ledger has to know about ablation to provide.
    /// </summary>
    public sealed class ResourceImpactLedger : IExposable
    {
        private readonly Dictionary<string, float> nutritionDeniedBySource =
            new Dictionary<string, float>(StringComparer.Ordinal);

        /// <summary>Adds <paramref name="amount"/> of denied nutrition to <paramref name="source"/>'s running
        /// total. A no-op for a non-positive amount or an unnamed source, so a caller that computed nothing
        /// this pass need not guard the call itself — exactly <see cref="DeathLedger.RecordAttributed"/>'s own
        /// shape.</summary>
        public void RecordNutritionDenied(string? source, float amount)
        {
            if (string.IsNullOrEmpty(source) || amount <= 0f) return;
            nutritionDeniedBySource.TryGetValue(source!, out float existing);
            nutritionDeniedBySource[source!] = existing + amount;
        }

        /// <summary>Nutrition denied and laid at <paramref name="source"/>'s door. Zero for a source that has
        /// never denied anything — the answer an ablated defect gives, and the answer every source gives
        /// before the game has run at all.</summary>
        public float NutritionDeniedBy(string? source) =>
            !string.IsNullOrEmpty(source) && nutritionDeniedBySource.TryGetValue(source!, out float n) ? n : 0f;

        /// <summary>Every source that has denied nutrition at least once, with its running total.</summary>
        public IReadOnlyDictionary<string, float> NutritionDeniedBySource => nutritionDeniedBySource;

        /// <summary>Every source's denial, summed.</summary>
        public float TotalNutritionDenied
        {
            get
            {
                float sum = 0f;
                foreach (float n in nutritionDeniedBySource.Values) sum += n;
                return sum;
            }
        }

        public override string ToString()
        {
            if (nutritionDeniedBySource.Count == 0) return "resource impact: none";

            var sb = new System.Text.StringBuilder("resource impact [");
            bool first = true;
            foreach (KeyValuePair<string, float> pair in nutritionDeniedBySource)
            {
                if (!first) sb.Append(", ");
                sb.Append(pair.Key).Append(' ')
                    .Append(pair.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Keyed rather than positional, exactly as <see cref="DeathLedger.ExposeData"/>'s own
        /// <c>deathsBySource</c> half is: a source is a defName, so there is no stable order to save it in,
        /// and content that stops shipping a source still loads — its number simply stops growing.</summary>
        public void ExposeData()
        {
            Dictionary<string, float>? map = new Dictionary<string, float>(nutritionDeniedBySource, StringComparer.Ordinal);
            Scribe_Collections.Look(ref map, "nutritionDeniedBySource", LookMode.Value, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                nutritionDeniedBySource.Clear();
                if (map != null)
                {
                    foreach (KeyValuePair<string, float> pair in map) nutritionDeniedBySource[pair.Key] = pair.Value;
                }
            }
        }
    }
}
