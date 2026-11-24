using System.Collections.Generic;

using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// One unfinished research project that gates the next era — part of an era's spine
    /// (<see cref="EraDef.SpineProjects"/>) — and where it stands.
    /// </summary>
    public sealed class EraGateLine
    {
        internal EraGateLine(string defName, string label, bool isCurrent, bool canStartNow,
            float progressPercent, IReadOnlyList<string> lockedBehind)
        {
            DefName = defName;
            Label = label;
            IsCurrent = isCurrent;
            CanStartNow = canStartNow;
            ProgressPercent = progressPercent;
            LockedBehind = lockedBehind;
        }

        /// <summary>The handle <see cref="GodCommands.SetResearchProject"/> takes.</summary>
        public string DefName { get; }

        public string Label { get; }

        /// <summary>Whether this is the project being researched right now.</summary>
        public bool IsCurrent { get; }

        /// <summary>Whether it can be picked now — the same test <see cref="ResearchProjectOption.CanStartNow"/>
        /// carries and <see cref="GodCommands.SetResearchProject"/> refuses against.</summary>
        public bool CanStartNow { get; }

        /// <summary>0-1.</summary>
        public float ProgressPercent { get; }

        /// <summary>Visible prerequisites still unfinished, by label; see
        /// <see cref="ResearchProjectOption.LockedBehind"/> for why hidden ones are never named.</summary>
        public IReadOnlyList<string> LockedBehind { get; }
    }

    /// <summary>
    /// How far the civilization is from its next era, counted the way the ladder actually turns.
    ///
    /// <para/><b>Why this exists beside <see cref="CivilizationSummary.EraProgress"/>.</b> That number is the
    /// share of <i>every</i> project tagged with the current era that is finished. An era is complete when its
    /// spine is finished (<see cref="EraDef.IsComplete"/>), and the spine is only the projects some other
    /// project depends on — so a playtester who had researched fifteen projects in 25 days saw "Sticks and
    /// stones" and a bar that said nothing about what would move it. This reads the frontier era's spine: how
    /// many of the projects that gate it are done, and which are still missing, by name.
    ///
    /// <para/>Counted on the <see cref="FrontierEraDefName"/> era, the earliest one not yet complete
    /// (<see cref="EraPacing.FrontierEra"/>) — the era the civilization is working toward. Until the first era
    /// is complete that is the first era itself, which is also what
    /// <see cref="CivilizationSummary.EraLabel"/> names at the start; afterwards it is the one after
    /// <see cref="ReachedEraDefName"/>.
    /// </summary>
    public sealed class EraOutlook
    {
        internal EraOutlook(
            string? reachedEraDefName, string? reachedEraLabel,
            string? frontierEraDefName, string? frontierEraLabel,
            int gatesFinished, int gatesTotal, bool gatesAreWholeEra,
            int eraProjectsFinished, int eraProjectsTotal,
            IReadOnlyList<EraGateLine> missingGates)
        {
            ReachedEraDefName = reachedEraDefName;
            ReachedEraLabel = reachedEraLabel;
            FrontierEraDefName = frontierEraDefName;
            FrontierEraLabel = frontierEraLabel;
            GatesFinished = gatesFinished;
            GatesTotal = gatesTotal;
            GatesAreWholeEra = gatesAreWholeEra;
            EraProjectsFinished = eraProjectsFinished;
            EraProjectsTotal = eraProjectsTotal;
            MissingGates = missingGates;
        }

        /// <summary>The last era the civilization has completed, in unbroken order from the first; null before
        /// even the first is complete.</summary>
        public string? ReachedEraDefName { get; }

        public string? ReachedEraLabel { get; }

        /// <summary>The era being worked toward — the earliest not yet complete. Null once the authored ladder
        /// is complete (endless research lies past it and has no era to reach).</summary>
        public string? FrontierEraDefName { get; }

        public string? FrontierEraLabel { get; }

        /// <summary>Gating projects of the frontier era already finished.</summary>
        public int GatesFinished { get; }

        /// <summary>Gating projects of the frontier era in all; 0 when there is no frontier.</summary>
        public int GatesTotal { get; }

        /// <summary>True when the frontier era has no spine and so every one of its projects gates it
        /// (<see cref="EraDef.IsComplete"/>'s fallback); the count above is then the whole era.</summary>
        public bool GatesAreWholeEra { get; }

        /// <summary>0-1: <see cref="GatesFinished"/> over <see cref="GatesTotal"/>, and 1 with no frontier. This
        /// reaches 1 exactly when the frontier era completes, which is the bar a player should be shown.</summary>
        public float Progress => GatesTotal == 0 ? 1f : (float)GatesFinished / GatesTotal;

        /// <summary>Every project of the frontier era finished, gating or not — the context line ("15 of 22
        /// researched"), never the progress.</summary>
        public int EraProjectsFinished { get; }

        public int EraProjectsTotal { get; }

        /// <summary>The gating projects still unfinished, in content order — "what advances the era". Empty
        /// exactly when <see cref="Progress"/> is 1.</summary>
        public IReadOnlyList<EraGateLine> MissingGates { get; }

        internal static EraOutlook Capture()
        {
            ResearchManager manager = Find.ResearchManager;
            EraDef? frontier = EraPacing.FrontierEra();
            EraDef? current = manager.CurrentEra;
            // CurrentEra falls back to the first era before it is complete; "reached" is only ever a complete one.
            EraDef? reached = current != null && current.IsComplete ? current : null;

            if (frontier == null)
            {
                return new EraOutlook(
                    reached?.defName, reached?.LabelCap, null, null, 0, 0, false, 0, 0, new List<EraGateLine>());
            }

            IReadOnlyList<ResearchProjectDef> spine = frontier.SpineProjects;
            bool whole = spine.Count == 0;
            IReadOnlyList<ResearchProjectDef> gates = whole ? frontier.Projects : spine;

            ResearchProjectDef? researching = manager.CurrentProj;
            int finished = 0;
            var missing = new List<EraGateLine>();
            for (int i = 0; i < gates.Count; i++)
            {
                ResearchProjectDef p = gates[i];
                if (p.IsFinished)
                {
                    finished++;
                    continue;
                }
                ResearchProjectOption option = ResearchProjectOption.For(p, p == researching);
                missing.Add(new EraGateLine(
                    p.defName, p.LabelCap, option.IsCurrent, option.CanStartNow, option.ProgressPercent,
                    option.LockedBehind));
            }

            int eraFinished = 0;
            IReadOnlyList<ResearchProjectDef> all = frontier.Projects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsFinished) eraFinished++;
            }

            return new EraOutlook(
                reached?.defName, reached?.LabelCap, frontier.defName, frontier.LabelCap,
                finished, gates.Count, whole, eraFinished, all.Count, missing);
        }
    }

    public sealed partial class GodViewSnapshot
    {
        private EraOutlook? era;

        /// <summary>
        /// What stands between the civilization and its next era — see <see cref="EraOutlook"/>. Built the
        /// first time it is read and cached on this snapshot, like <see cref="Research"/>.
        /// </summary>
        public EraOutlook Era => era ??= EraOutlook.Capture();
    }
}
