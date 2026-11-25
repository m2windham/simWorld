using System.Collections.Generic;

using SimWorld.Defs;
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

    /// <summary>One thing besides research the era asks before the civilization leaves it
    /// (<see cref="EraDef.conditions"/>), and where it stands.</summary>
    public sealed class EraConditionLine
    {
        internal EraConditionLine(string label, float current, float target, bool met)
        {
            Label = label;
            Current = current;
            Target = target;
            IsMet = met;
        }

        /// <summary>What is asked, in the player's words.</summary>
        public string Label { get; }

        /// <summary>The reading now, in the units of <see cref="Target"/> (a share, days of food, people,
        /// buildings).</summary>
        public float Current { get; }

        public float Target { get; }

        public bool IsMet { get; }
    }

    /// <summary>
    /// The era the civilization is in, what it takes to leave it, and what entering it changed.
    ///
    /// <para/><b>The era model</b> (<c>docs/design/era-progression.md</c> §1): the civilization is in
    /// <see cref="CurrentEraDefName"/> (<see cref="ResearchManager.CurrentEra"/>) while it learns that era's
    /// research; it works toward <see cref="NextEraDefName"/>, which it enters once the current era's research
    /// spine is finished and its conditions are met. The gates counted here are the current era's.
    ///
    /// <para/><b>Why this exists beside <see cref="CivilizationSummary.EraProgress"/>.</b> That number is the
    /// share of <i>every</i> project tagged with the current era that is finished. The era turns on its spine
    /// (<see cref="EraDef.IsComplete"/>), the projects some other project depends on, and its conditions — so a
    /// playtester who had researched fifteen projects in 25 days saw "Sticks and stones" and a bar that said
    /// nothing about what would move it. This lists what is missing, by name.
    ///
    /// <para/>The older names stay for the host that binds them: <see cref="FrontierEraDefName"/> is the era whose
    /// gates are counted, which is now the current era, and <see cref="ReachedEraDefName"/> is the era mastered,
    /// the one before it.
    /// </summary>
    public sealed class EraOutlook
    {
        internal EraOutlook(
            EraDef? current, EraDef? next, EraDef? mastered,
            int gatesFinished, int gatesTotal, bool gatesAreWholeEra,
            int eraProjectsFinished, int eraProjectsTotal,
            IReadOnlyList<EraGateLine> missingGates,
            IReadOnlyList<EraConditionLine> conditions,
            EraDef? rivalEra, EraDef? rivalArmsEra)
        {
            CurrentEraDefName = current?.defName;
            CurrentEraLabel = current?.LabelCap;
            NextEraDefName = next?.defName;
            NextEraLabel = next?.LabelCap;
            ReachedEraDefName = mastered?.defName;
            ReachedEraLabel = mastered?.LabelCap;
            FrontierEraDefName = next == null && current != null && current.IsComplete ? null : current?.defName;
            FrontierEraLabel = FrontierEraDefName == null ? null : current?.LabelCap;
            GatesFinished = gatesFinished;
            GatesTotal = gatesTotal;
            GatesAreWholeEra = gatesAreWholeEra;
            EraProjectsFinished = eraProjectsFinished;
            EraProjectsTotal = eraProjectsTotal;
            MissingGates = missingGates;
            Conditions = conditions;
            RivalEraDefName = rivalEra?.defName;
            RivalEraLabel = rivalEra?.LabelCap;
            RivalArmsEraDefName = rivalArmsEra?.defName;
            RivalLead = rivalEra != null && current != null ? rivalEra.order - current.order : 0;

            var edicts = new List<string>();
            var buildings = new List<string>();
            var houses = new List<string>();
            if (current != null)
            {
                foreach (God.EdictDef e in EraUnlocks.EdictsOpened(current)) edicts.Add(e.defName);
                foreach (ThingDef t in EraUnlocks.Buildings(current)) buildings.Add(t.defName);
                foreach (Building.DwellingDef d in EraUnlocks.Dwellings(current)) houses.Add(d.defName);
            }
            EdictsOpenedHere = edicts;
            BuildingsOfThisEra = buildings;
            DwellingsOfThisEra = houses;
            NewInThisEra = current == null ? new List<string>() : EraUnlocks.Describe(current);
        }

        /// <summary>The era the civilization is in: learning its research, issuing its edicts.</summary>
        public string? CurrentEraDefName { get; }

        public string? CurrentEraLabel { get; }

        /// <summary>The era it is working toward; null in the last era.</summary>
        public string? NextEraDefName { get; }

        public string? NextEraLabel { get; }

        /// <summary>The era mastered: the one before the current era; null in the first.</summary>
        public string? ReachedEraDefName { get; }

        public string? ReachedEraLabel { get; }

        /// <summary>The era whose gates are counted: the current era, until the last era's research is done.</summary>
        public string? FrontierEraDefName { get; }

        public string? FrontierEraLabel { get; }

        /// <summary>Gating projects of the current era already finished.</summary>
        public int GatesFinished { get; }

        /// <summary>Gating projects of the current era in all.</summary>
        public int GatesTotal { get; }

        /// <summary>True when the current era has no spine and so every one of its projects gates it
        /// (<see cref="EraDef.IsComplete"/>'s fallback); the count above is then the whole era.</summary>
        public bool GatesAreWholeEra { get; }

        /// <summary>The conditions of the current era, met or not, in content order.</summary>
        public IReadOnlyList<EraConditionLine> Conditions { get; }

        /// <summary>Every condition of the current era holds.</summary>
        public bool ConditionsMet
        {
            get
            {
                for (int i = 0; i < Conditions.Count; i++)
                {
                    if (!Conditions[i].IsMet) return false;
                }
                return true;
            }
        }

        /// <summary>0-1: gates finished and conditions met over gates and conditions in all; 1 with nothing
        /// asked. Reaches 1 when the way into the next era is open, which is the bar a player should be shown.</summary>
        public float Progress
        {
            get
            {
                int total = GatesTotal + Conditions.Count;
                if (total == 0) return 1f;
                int done = GatesFinished;
                for (int i = 0; i < Conditions.Count; i++)
                {
                    if (Conditions[i].IsMet) done++;
                }
                return (float)done / total;
            }
        }

        /// <summary>Every project of the current era finished, gating or not — the context line ("15 of 22
        /// researched"), never the progress.</summary>
        public int EraProjectsFinished { get; }

        public int EraProjectsTotal { get; }

        /// <summary>The gating projects still unfinished, in content order. Empty once the research is done,
        /// whatever the conditions say.</summary>
        public IReadOnlyList<EraGateLine> MissingGates { get; }

        /// <summary>The edicts the current era opened (<see cref="God.EdictDef.requiredEra"/>), by defName.</summary>
        public IReadOnlyList<string> EdictsOpenedHere { get; }

        /// <summary>Buildings whose research belongs to the current era, by defName.</summary>
        public IReadOnlyList<string> BuildingsOfThisEra { get; }

        /// <summary>Houses of the current era (<see cref="Building.DwellingDef.era"/>), by defName.</summary>
        public IReadOnlyList<string> DwellingsOfThisEra { get; }

        /// <summary>The era-turn letter's own lines about the current era (<see cref="EraUnlocks.Describe"/>).</summary>
        public IReadOnlyList<string> NewInThisEra { get; }

        /// <summary>The era rival civilizations are in (<see cref="Factions.FactionAdvancement.RivalEra"/>).</summary>
        public string? RivalEraDefName { get; }

        public string? RivalEraLabel { get; }

        /// <summary>The era whose raiders the rivals field: the one before theirs, as the civilization's own arms
        /// are the era it has mastered. Null while they have mastered none.</summary>
        public string? RivalArmsEraDefName { get; }

        /// <summary>Eras the rivals are ahead (positive) or behind (negative) of the civilization.</summary>
        public int RivalLead { get; }

        private RenewalSummary? renewal;

        /// <summary>How far the town has rebuilt itself in this era's forms (<see cref="RenewalSummary"/>). Built
        /// the first time it is read, like <see cref="GodViewSnapshot.Era"/> itself.</summary>
        public RenewalSummary Renewal => renewal ??= RenewalSummary.Capture();

        internal static EraOutlook Capture()
        {
            ResearchManager manager = Find.ResearchManager;
            EraDef? current = manager.CurrentEra;
            EraDef? next = manager.NextEra;
            EraDef? mastered = manager.MasteredEra;
            EraDef? rival = Factions.FactionAdvancement.RivalEra();
            EraDef? rivalArms = Factions.FactionAdvancement.ArmsEra(rival);

            if (current == null)
            {
                return new EraOutlook(null, null, null, 0, 0, false, 0, 0, new List<EraGateLine>(),
                    new List<EraConditionLine>(), rival, rivalArms);
            }

            IReadOnlyList<ResearchProjectDef> spine = current.SpineProjects;
            bool whole = spine.Count == 0;
            IReadOnlyList<ResearchProjectDef> gates = whole ? current.Projects : spine;

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
            IReadOnlyList<ResearchProjectDef> all = current.Projects;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].IsFinished) eraFinished++;
            }

            // The last era leads nowhere: nothing to leave it for, so nothing it asks.
            var conditions = new List<EraConditionLine>();
            if (next != null && current.conditions.Count > 0)
            {
                EraConditionContext context = EraConditionContext.Now();
                for (int i = 0; i < current.conditions.Count; i++)
                {
                    EraCondition c = current.conditions[i];
                    conditions.Add(new EraConditionLine(c.Label, c.Current(context), c.Target, c.IsMet(context)));
                }
            }

            return new EraOutlook(current, next, mastered, finished, gates.Count, whole, eraFinished, all.Count,
                missing, conditions, rival, rivalArms);
        }
    }

    public sealed partial class GodViewSnapshot
    {
        private EraOutlook? era;

        /// <summary>
        /// The era the civilization is in and what stands between it and the next — see <see cref="EraOutlook"/>.
        /// Built the first time it is read and cached on this snapshot, like <see cref="Research"/>.
        /// </summary>
        public EraOutlook Era => era ??= EraOutlook.Capture();
    }
}
