using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.God.View
{
    /// <summary>One column of the work tab: a work type, by defName (RimWorld: a <c>WorkTypeDef</c> column in
    /// <c>MainTabWindow_Work</c>).</summary>
    public sealed class WorkTypeColumn
    {
        internal WorkTypeColumn(string defName, string label, string labelShort, int naturalPriority,
            IReadOnlyList<string> relevantSkillDefNames)
        {
            DefName = defName;
            Label = label;
            LabelShort = labelShort;
            NaturalPriority = naturalPriority;
            RelevantSkillDefNames = relevantSkillDefNames;
        }

        /// <summary>The handle <see cref="GodCommands.SetWorkPriority"/> takes.</summary>
        public string DefName { get; }

        public string Label { get; }

        /// <summary>The column header (<c>WorkTypeDef.labelShort</c>, falling back to the label).</summary>
        public string LabelShort { get; }

        /// <summary>The order work types are tried in among equal priorities, higher first — the order these
        /// columns are listed in, as RimWorld's work tab lists them.</summary>
        public int NaturalPriority { get; }

        /// <summary>The skills this work uses (<c>WorkTypeDef.relevantSkills</c>), to show beside a cell.</summary>
        public IReadOnlyList<string> RelevantSkillDefNames { get; }
    }

    /// <summary>One citizen's setting for one work type.</summary>
    public sealed class WorkPriorityCell
    {
        internal WorkPriorityCell(string workTypeDefName, int priority, bool disabled, string? disabledReason)
        {
            WorkTypeDefName = workTypeDefName;
            Priority = priority;
            Disabled = disabled;
            DisabledReason = disabledReason;
        }

        public string WorkTypeDefName { get; }

        /// <summary>
        /// 0 (never does it) or 1-4, 1 first — the priority the AI actually scans by
        /// (<c>Pawn_WorkSettings.GetPriority</c>). For a citizen whose <see cref="WorkPriorityRow.ManualPriorities"/>
        /// is off, every enabled cell reads 3: the simulation is not ranking their work, only switching it on and
        /// off, exactly as RimWorld's work tab with manual priorities unticked.
        /// </summary>
        public int Priority { get; }

        /// <summary>They cannot do this work at all; <see cref="Priority"/> is 0 and setting it is refused.</summary>
        public bool Disabled { get; }

        /// <summary>Why, in the core's words — the trait, backstory or gene that bars it. Null when not
        /// disabled. Display verbatim.</summary>
        public string? DisabledReason { get; }
    }

    /// <summary>One citizen's row of the work tab.</summary>
    public sealed class WorkPriorityRow
    {
        internal WorkPriorityRow(int thingId, string name, bool manualPriorities, string? roleDefName,
            IReadOnlyList<WorkPriorityCell> cells)
        {
            ThingId = thingId;
            Name = name;
            ManualPriorities = manualPriorities;
            RoleDefName = roleDefName;
            Cells = cells;
        }

        /// <summary>The citizen's id, as <see cref="CitizenLine.ThingId"/> carries it.</summary>
        public int ThingId { get; }

        public string Name { get; }

        /// <summary>
        /// Whether this citizen's work is ranked 1-4 or only switched on and off
        /// (<c>Pawn_WorkSettings.useWorkPriorities</c>). RimWorld makes this one switch for the whole colony;
        /// this port keeps it per person, and it comes on for anyone given an office's role or a numbered
        /// priority through <see cref="GodCommands.SetWorkPriority"/>.
        /// </summary>
        public bool ManualPriorities { get; }

        /// <summary>Their standing work role (<c>Pawn_WorkSettings.Role</c>, a <see cref="RoleOption.DefName"/>),
        /// or null for none — the rule that put this row's emphasized cells at the top. The same reading as
        /// <see cref="CitizenLine.RoleDefName"/>.</summary>
        public string? RoleDefName { get; }

        /// <summary>One cell per <see cref="WorkPriorityTable.WorkTypes"/> column, in the same order.</summary>
        public IReadOnlyList<WorkPriorityCell> Cells { get; }
    }

    /// <summary>
    /// A settlement's work tab (RimWorld: <c>MainTabWindow_Work</c>): every work type as a column, every
    /// citizen with a body as a row, and in each cell the priority the AI scans by. The write half is
    /// <see cref="GodCommands.SetWorkPriority"/>.
    ///
    /// <para/><b>The lever, and what carries it.</b> <c>JobGiver_Work</c> walks
    /// <c>Pawn_WorkSettings.WorkGiversInOrderNormal</c>, ordered by exactly these priorities, so a change here
    /// changes what the citizen picks up next. Two things sit above it in the think tree and the player
    /// should know: needs (hunger, rest, recreation) and an active edict, which asks every citizen it
    /// applies to for its work types ahead of routine work and does not consult a priority of 0 — an edict
    /// is the god's own standing order, and outranks a per-person setting by design of the think tree.
    /// </summary>
    public sealed class WorkPriorityTable
    {
        internal WorkPriorityTable(int ticksGame, int settlementTile, IReadOnlyList<WorkTypeColumn> workTypes,
            IReadOnlyList<WorkPriorityRow> rows)
        {
            TicksGame = ticksGame;
            SettlementTile = settlementTile;
            WorkTypes = workTypes;
            Rows = rows;
        }

        /// <summary>Highest number a cell can hold (<c>Pawn_WorkSettings.LowestPriority</c>); 0 is "never".</summary>
        public const int LowestPriority = Pawn_WorkSettings.LowestPriority;

        public int TicksGame { get; }

        public int SettlementTile { get; }

        /// <summary>Columns, by <see cref="WorkTypeColumn.NaturalPriority"/> descending as RimWorld's tab
        /// orders them, ties in def order.</summary>
        public IReadOnlyList<WorkTypeColumn> WorkTypes { get; }

        /// <summary>One row per live citizen with a <c>Pawn</c>, in roster order.</summary>
        public IReadOnlyList<WorkPriorityRow> Rows { get; }

        internal static List<WorkTypeDef> WorkTypesInPriorityOrder() =>
            DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderByDescending(w => w.naturalPriority)
                .ToList();

        internal static WorkPriorityTable Of(World.Settlement settlement, int ticksGame)
        {
            List<WorkTypeDef> types = WorkTypesInPriorityOrder();
            var columns = new List<WorkTypeColumn>(types.Count);
            for (int i = 0; i < types.Count; i++)
            {
                WorkTypeDef w = types[i];
                IReadOnlyList<string> skills = w.relevantSkills == null
                    ? Array.Empty<string>()
                    : w.relevantSkills.Select(s => s.defName).ToArray();
                columns.Add(new WorkTypeColumn(
                    w.defName, w.LabelCap, string.IsNullOrEmpty(w.labelShort) ? w.LabelCap : w.labelShort!,
                    w.naturalPriority, skills));
            }

            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            var rows = new List<WorkPriorityRow>(citizens.Count);
            for (int c = 0; c < citizens.Count; c++)
            {
                Pawn p = citizens[c];
                if (p.Dead) continue;

                var cells = new List<WorkPriorityCell>(types.Count);
                for (int i = 0; i < types.Count; i++)
                {
                    string? reason = WorkDisabledReason.For(p, types[i]);
                    int priority = reason != null || p.workSettings == null ? 0 : p.workSettings.GetPriority(types[i]);
                    cells.Add(new WorkPriorityCell(types[i].defName, priority, reason != null, reason));
                }
                rows.Add(new WorkPriorityRow(
                    p.thingIDNumber, p.Label, p.workSettings?.useWorkPriorities ?? false, p.workSettings?.Role?.defName, cells));
            }

            return new WorkPriorityTable(ticksGame, settlement.tile, columns, rows);
        }
    }

    /// <summary>
    /// Why a pawn cannot do a work type, in words, or null when they can — the reading
    /// <see cref="Pawn.WorkTypeIsDisabled"/> gives as a bool, taken apart into the traits, backstories and
    /// genes that <see cref="Pawn.CombinedDisabledWorkTags"/> folds together (RimWorld:
    /// <c>Pawn.GetReasonsForDisabledWorkType</c>, which a work-tab tooltip shows). Every source that bars the
    /// work is named, not just the first.
    /// </summary>
    internal static class WorkDisabledReason
    {
        internal static string? For(Pawn pawn, WorkTypeDef workType)
        {
            if (!pawn.WorkTypeIsDisabled(workType)) return null;

            string work = workType.gerundLabel ?? workType.label ?? workType.defName;
            if (!pawn.RaceProps.Humanlike) return pawn.Label + " is not a person who can be given work.";

            var sources = new List<string>();
            foreach (BackstoryDef story in pawn.story.AllBackstories)
            {
                if (Bars(story.workDisables, workType)) sources.Add("their past as " + (story.title ?? story.defName));
            }
            List<Trait> traits = pawn.story.traits.allTraits;
            for (int i = 0; i < traits.Count; i++)
            {
                if (Bars(traits[i].def.DisabledWorkTagsAtDegree(traits[i].degree), workType))
                {
                    sources.Add("being " + traits[i].Label);
                }
            }
            if (pawn.genes != null)
            {
                IReadOnlyList<Pawns.Genes.Gene> genes = pawn.genes.ActiveGenesListForReading;
                for (int i = 0; i < genes.Count; i++)
                {
                    if (Bars(genes[i].def.disabledWorkTags, workType))
                    {
                        sources.Add("their " + (genes[i].def.label ?? genes[i].def.defName) + " gene");
                    }
                }
            }

            return sources.Count == 0
                ? pawn.Label + " is incapable of " + work + "."
                : pawn.Label + " is incapable of " + work + " because of " + string.Join(" and ", sources) + ".";
        }

        private static bool Bars(WorkTags disables, WorkTypeDef workType) =>
            disables != WorkTags.None
            && ((disables & WorkTags.AllWork) != WorkTags.None || (workType.workTags & disables) != WorkTags.None);
    }
}
