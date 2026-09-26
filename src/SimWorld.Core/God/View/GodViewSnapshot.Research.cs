using System.Collections.Generic;

using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// One research project as the view needs it: what it costs, whether it is current, whether it can be
    /// started right now, and — when it cannot — what visible prerequisite still stands in the way. The same
    /// shape <see cref="EdictOption"/> gives an edict, one system over.
    ///
    /// <para/><b>Locked behind only ever names visible prerequisites.</b>
    /// <see cref="ResearchProjectDef.hiddenPrerequisites"/> exist specifically to gate a project "without
    /// being drawn on the tree" — see that field's own doc — and naming one here would out a fact the
    /// simulation deliberately keeps off the tree. A project blocked purely by a hidden prerequisite therefore
    /// reports <see cref="CanStartNow"/> false with an empty <see cref="LockedBehind"/>, which mirrors exactly
    /// what a player would see of it in the simulation's own tree.
    /// </summary>
    public sealed class ResearchProjectOption
    {
        internal ResearchProjectOption(
            string defName, string label, string description, float cost, string? eraDefName,
            bool isCurrent, bool isFinished, bool canStartNow, float progressPercent,
            IReadOnlyList<string> lockedBehind)
        {
            DefName = defName;
            Label = label;
            Description = description;
            Cost = cost;
            EraDefName = eraDefName;
            IsCurrent = isCurrent;
            IsFinished = isFinished;
            CanStartNow = canStartNow;
            ProgressPercent = progressPercent;
            LockedBehind = lockedBehind;
        }

        /// <summary>The handle to pass to <see cref="GodCommands.SetResearchProject"/>. Not a
        /// <see cref="ResearchProjectDef"/> — see <see cref="GodViewSnapshot"/> on why the seam is a
        /// string.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string Description { get; }

        /// <summary>Raw research-point cost, before any researcher's own <see cref="ResearchProjectDef.CostFactor"/>
        /// — the number on the tree, not what any one researcher would actually pay.</summary>
        public float Cost { get; }

        /// <summary>The era this project belongs to, or null when content leaves it untagged.</summary>
        public string? EraDefName { get; }

        /// <summary>Whether this is <see cref="ResearchManager.CurrentProj"/> right now.</summary>
        public bool IsCurrent { get; }

        public bool IsFinished { get; }

        /// <summary>Not finished, and every visible and hidden prerequisite is — the same test
        /// <see cref="GodCommands.SetResearchProject"/> refuses against, so a view can never offer a pick the
        /// command would then refuse.</summary>
        public bool CanStartNow { get; }

        /// <summary>0-1, meaningless past 1 (see <see cref="ResearchManager.ProgressPercent"/>).</summary>
        public float ProgressPercent { get; }

        /// <summary>The visible prerequisites still unfinished, by label — what this project is "locked
        /// behind". Empty when it can be started now, when it is finished, or when it is blocked only by a
        /// hidden prerequisite the tree does not draw — see the class doc.</summary>
        public IReadOnlyList<string> LockedBehind { get; }

        internal static ResearchProjectOption For(ResearchProjectDef def, bool isCurrent)
        {
            var locked = new List<string>();
            if (!def.CanStartNow && !def.IsFinished && def.prerequisites != null)
            {
                for (int i = 0; i < def.prerequisites.Count; i++)
                {
                    ResearchProjectDef? p = def.prerequisites[i];
                    if (p != null && !p.IsFinished) locked.Add(p.LabelCap);
                }
            }

            return new ResearchProjectOption(
                def.defName, def.LabelCap, def.description ?? "", def.baseCost, def.era?.defName,
                isCurrent, def.IsFinished, def.CanStartNow, def.ProgressPercent, locked);
        }
    }

    /// <summary>
    /// What the civilization is studying, laid out for a player to pick from — the read half of the research
    /// lever, without which <see cref="GodCommands.SetResearchProject"/> would be a command naming things the
    /// player cannot see (<c>docs/design/player-first.md</c> §9: "a number nobody acts on is decoration").
    /// </summary>
    public sealed class ResearchOverview
    {
        internal ResearchOverview(
            string? currentProjectDefName, float currentProjectProgressPercent,
            IReadOnlyList<ResearchProjectOption> projects)
        {
            CurrentProjectDefName = currentProjectDefName;
            CurrentProjectProgressPercent = currentProjectProgressPercent;
            Projects = projects;
        }

        /// <summary>The defName of <see cref="ResearchManager.CurrentProj"/>, or null when nothing is
        /// current.</summary>
        public string? CurrentProjectDefName { get; }

        /// <summary>0-1, or 0 when nothing is current.</summary>
        public float CurrentProjectProgressPercent { get; }

        /// <summary>Every loaded project — current, available, finished, and locked alike — so a host can
        /// draw the whole tree from one read rather than asking three different questions.</summary>
        public IReadOnlyList<ResearchProjectOption> Projects { get; }

        internal static ResearchOverview Capture()
        {
            ResearchManager manager = Find.ResearchManager;
            ResearchProjectDef? current = manager.CurrentProj;

            IReadOnlyList<ResearchProjectDef> all = manager.AllProjects;
            var projects = new List<ResearchProjectOption>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                ResearchProjectDef def = all[i];
                projects.Add(ResearchProjectOption.For(def, def == current));
            }

            return new ResearchOverview(
                current?.defName, current == null ? 0f : manager.ProgressPercent(current), projects);
        }
    }

    public sealed partial class GodViewSnapshot
    {
        private ResearchOverview? research;

        /// <summary>
        /// What the civilization is researching, what it could start now, and what is locked behind what.
        ///
        /// <para/><b>Why this is lazily computed and cached rather than threaded through <see cref="Capture(int)"/>'s
        /// constructor, the way <see cref="Losses"/> or <see cref="Conditions"/> are.</b> That constructor
        /// lives in <c>GodViewSnapshot.cs</c>, which this lane does not touch — another lane's own partial file
        /// (<c>GodViewSnapshot.Letters.cs</c>) already states the reason: a private constructor a partial file
        /// does not declare cannot be widened from here without editing the shared file CLAUDE.md says not to
        /// (see that class's <see cref="PendingLetters"/> for the identical shape, one lane over). The cache
        /// means two reads of the same snapshot object always agree with each other even if the simulation
        /// ticks in between; it does not guarantee agreement with the instant <see cref="Capture(int)"/> itself
        /// ran if nothing reads this until later, which in practice nothing in this codebase does.
        /// </summary>
        public ResearchOverview Research => research ??= ResearchOverview.Capture();
    }
}
