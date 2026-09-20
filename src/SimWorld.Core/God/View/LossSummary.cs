using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Director;
using SimWorld.Pawns;

namespace SimWorld.God.View
{
    /// <summary>
    /// One cause of death and how many of the civilization's people it has claimed — a value-only mirror of
    /// <see cref="DeathLedger.this[SimWorld.Pawns.DeathCause]"/>. Sparse: a cause that has killed nobody is
    /// absent rather than listed at zero, matching every other readout on this seam.
    /// </summary>
    public sealed class DeathCauseLine
    {
        internal DeathCauseLine(DeathCause cause, int count)
        {
            Cause = cause;
            Count = count;
        }

        /// <summary>What they died of — the small closed enum <see cref="DeathLedger"/> already counts by,
        /// carried through rather than turned into a string. It names no worker and resolves to nothing, so
        /// the handle rule this seam exists for (never hand out a <c>Def</c>) has nothing to say about it; a
        /// host switches on it directly instead of re-parsing text it would otherwise have to invent.</summary>
        public DeathCause Cause { get; }

        public int Count { get; }

        /// <summary>Every cause that has killed at least once, in <see cref="DeathCause"/>'s declared order.</summary>
        internal static List<DeathCauseLine> AllOf(DeathLedger deaths)
        {
            var lines = new List<DeathCauseLine>();
            foreach (DeathCause cause in (DeathCause[])Enum.GetValues(typeof(DeathCause)))
            {
                int count = deaths[cause];
                if (count > 0) lines.Add(new DeathCauseLine(cause, count));
            }
            return lines;
        }
    }

    /// <summary>
    /// One source's toll — what killed them, as distinct from what they died of — keyed by the source's own
    /// defName (<c>"ManhunterPack"</c>, <c>"Drought"</c>, <c>"Disease_Plague"</c>, ...). A mirror of
    /// <see cref="DeathLedger.BySource"/>; sparse for the same reason that dictionary is.
    /// </summary>
    public sealed class DeathSourceLine
    {
        internal DeathSourceLine(string source, int count)
        {
            Source = source;
            Count = count;
        }

        /// <summary>The killer's defName — a handle the host already has content for, never resolved to a Def
        /// on this side of the seam.</summary>
        public string Source { get; }

        public int Count { get; }

        /// <summary>Every source that has killed at least once, ordered by defName so the list reads the same
        /// way on every capture rather than trailing whatever order the ledger's dictionary happens to hold.</summary>
        internal static List<DeathSourceLine> AllOf(DeathLedger deaths) =>
            deaths.BySource
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new DeathSourceLine(pair.Key, pair.Value))
                .ToList();
    }

    /// <summary>
    /// Nutrition one source has denied to production, keyed by defName. A mirror of
    /// <see cref="ResourceImpactLedger.NutritionDeniedBySource"/>.
    /// </summary>
    public sealed class NutritionLossLine
    {
        internal NutritionLossLine(string source, float nutritionDenied)
        {
            Source = source;
            NutritionDenied = nutritionDenied;
        }

        public string Source { get; }

        public float NutritionDenied { get; }

        internal static List<NutritionLossLine> AllOf(ResourceImpactLedger resourceImpact) =>
            resourceImpact.NutritionDeniedBySource
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new NutritionLossLine(pair.Key, pair.Value))
                .ToList();
    }

    /// <summary>
    /// Structures one source has destroyed, keyed by defName. A mirror of
    /// <see cref="ResourceImpactLedger.StructuresDestroyedBySource"/>.
    /// </summary>
    public sealed class StructureLossLine
    {
        internal StructureLossLine(string source, int structuresDestroyed)
        {
            Source = source;
            StructuresDestroyed = structuresDestroyed;
        }

        public string Source { get; }

        public int StructuresDestroyed { get; }

        internal static List<StructureLossLine> AllOf(ResourceImpactLedger resourceImpact) =>
            resourceImpact.StructuresDestroyedBySource
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new StructureLossLine(pair.Key, pair.Value))
                .ToList();
    }

    /// <summary>
    /// What this civilization has lost, and to what — <see cref="DeathLedger"/> and
    /// <see cref="ResourceImpactLedger"/> mirrored onto the seam exactly as they stand
    /// (<c>docs/spec/simworld-spec.md</c> §12a). Both ledgers are uncapped and outlive the condition or raid
    /// that ran up their toll — see either class's own doc — so this is the one place on the seam that can
    /// still answer "how much has this run cost us" once a game is old enough that the chronicle has already
    /// forgotten the events themselves.
    ///
    /// <para/>Every by-cause and by-source list here is sparse: a cause or a source that has never acted is
    /// simply absent, not a row of zeroes — the same convention <see cref="DeathLedger"/> and
    /// <see cref="ResourceImpactLedger"/> already keep, carried through rather than flattened. The three
    /// totals (<see cref="TotalDeaths"/>, <see cref="TotalNutritionDenied"/>,
    /// <see cref="TotalStructuresDestroyed"/>) are each ledger's own running sum, read straight off it rather
    /// than recomputed here.
    /// </summary>
    public sealed class LossSummary
    {
        internal LossSummary(
            int totalDeaths, IReadOnlyList<DeathCauseLine> deathsByCause, IReadOnlyList<DeathSourceLine> deathsBySource,
            float totalNutritionDenied, IReadOnlyList<NutritionLossLine> nutritionDeniedBySource,
            int totalStructuresDestroyed, IReadOnlyList<StructureLossLine> structuresDestroyedBySource)
        {
            TotalDeaths = totalDeaths;
            DeathsByCause = deathsByCause;
            DeathsBySource = deathsBySource;
            TotalNutritionDenied = totalNutritionDenied;
            NutritionDeniedBySource = nutritionDeniedBySource;
            TotalStructuresDestroyed = totalStructuresDestroyed;
            StructuresDestroyedBySource = structuresDestroyedBySource;
        }

        /// <summary>Every death this civilization has recorded, whatever the cause — <see cref="DeathLedger.Total"/>.</summary>
        public int TotalDeaths { get; }

        /// <summary>What they died of. See <see cref="DeathCauseLine"/>.</summary>
        public IReadOnlyList<DeathCauseLine> DeathsByCause { get; }

        /// <summary>What killed them, as distinct from what they died of. See <see cref="DeathSourceLine"/>.</summary>
        public IReadOnlyList<DeathSourceLine> DeathsBySource { get; }

        /// <summary>Nutrition denied to production by every source, summed — <see cref="ResourceImpactLedger.TotalNutritionDenied"/>.</summary>
        public float TotalNutritionDenied { get; }

        /// <summary>Nutrition denied, by source. See <see cref="NutritionLossLine"/>.</summary>
        public IReadOnlyList<NutritionLossLine> NutritionDeniedBySource { get; }

        /// <summary>Structures destroyed by every source, summed — <see cref="ResourceImpactLedger.TotalStructuresDestroyed"/>.</summary>
        public int TotalStructuresDestroyed { get; }

        /// <summary>Structures destroyed, by source. See <see cref="StructureLossLine"/>.</summary>
        public IReadOnlyList<StructureLossLine> StructuresDestroyedBySource { get; }

        internal static LossSummary From(DeathLedger deaths, ResourceImpactLedger resourceImpact) =>
            new LossSummary(
                deaths.Total, DeathCauseLine.AllOf(deaths), DeathSourceLine.AllOf(deaths),
                resourceImpact.TotalNutritionDenied, NutritionLossLine.AllOf(resourceImpact),
                resourceImpact.TotalStructuresDestroyed, StructureLossLine.AllOf(resourceImpact));
    }
}
