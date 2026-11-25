using System.Collections.Generic;

namespace SimWorld.Map.View
{
    /// <summary>
    /// What the player may build in the open settlement now (host UX request after playtest 4: the Build tab in
    /// the Bronze age listed only the bed, the walls, the door and the storage hut, because the host kept its own
    /// hand-written list of the first era's buildables and could not ask for more).
    ///
    /// <para/><b>Gated the way the simulation gates.</b> An option is listed when a blueprint can be placed for it
    /// and either the civilization has learned to build it or its era has been reached, so a bronze-age town sees
    /// the bronze age's buildings it has still to research, greyed with the reason, and never an iron-age one.
    /// <see cref="BuildableOption.CanPlace"/> is <see cref="MapCommands.PlaceBlueprint"/>'s own answer: the list
    /// and the command read one rule (<c>BuildGate</c>), so an option offered is never refused for its research,
    /// and the command refuses anything the list does not offer, with the same sentence as
    /// <see cref="BuildableOption.Reason"/>.
    ///
    /// <para/>A slow-clock capture like <see cref="SettlementLevers"/>: read it when the Build tab opens and after
    /// a research project or an era turn, not every frame.
    /// </summary>
    public sealed class BuildCatalogue
    {
        internal BuildCatalogue(
            bool hasMap, string? absenceReason, int ticksGame, string? eraDefName, string? eraLabel,
            IReadOnlyList<BuildableOption> options)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            TicksGame = ticksGame;
            EraDefName = eraDefName;
            EraLabel = eraLabel;
            Options = options;
        }

        /// <summary>False when no interior is open; <see cref="AbsenceReason"/> says why and
        /// <see cref="Options"/> is empty.</summary>
        public bool HasMap { get; }

        public string? AbsenceReason { get; }

        public int TicksGame { get; }

        /// <summary>The era the town is in (<c>ResearchManager.CurrentEra</c>), the one the list is gated by.</summary>
        public string? EraDefName { get; }

        public string? EraLabel { get; }

        /// <summary>Every buildable on offer, oldest era first, then by label. Placeable or not, see
        /// <see cref="BuildableOption.CanPlace"/>.</summary>
        public IReadOnlyList<BuildableOption> Options { get; }

        /// <summary>The option for <paramref name="defName"/>, or null when it is not on offer.</summary>
        public BuildableOption? Find(string defName)
        {
            for (int i = 0; i < Options.Count; i++)
            {
                if (Options[i].DefName == defName) return Options[i];
            }
            return null;
        }
    }

    /// <summary>One thing the player may place a blueprint for: what it is, how big, what it costs and which age
    /// it belongs to.</summary>
    public sealed class BuildableOption
    {
        internal BuildableOption(
            string defName, string label, string? description, IntVec2 size,
            string? eraDefName, string? eraLabel, bool canPlace, string? reason,
            IReadOnlyList<string> researchNeeded, IReadOnlyList<MaterialCost> cost, int stuffCount,
            IReadOnlyList<MaterialCost> stuffs, string? defaultStuffDefName)
        {
            DefName = defName;
            Label = label;
            Description = description;
            Size = size;
            EraDefName = eraDefName;
            EraLabel = eraLabel;
            CanPlace = canPlace;
            Reason = reason;
            ResearchNeeded = researchNeeded;
            Cost = cost;
            StuffCount = stuffCount;
            Stuffs = stuffs;
            DefaultStuffDefName = defaultStuffDefName;
        }

        /// <summary>The handle <see cref="MapCommands.PlaceBlueprint"/> takes.</summary>
        public string DefName { get; }

        public string Label { get; }

        public string? Description { get; }

        /// <summary>Footprint in cells facing North (a bed is 1x2). Any of the four rotations may be placed: no
        /// def here forbids one (ThingDef.rotatable is a known dormant seam, Wiring/dormant-seams.txt), so the
        /// option carries no rotatable flag that would only ever read false.</summary>
        public IntVec2 Size { get; }

        /// <summary>The era its research belongs to, as an <c>EraDef</c> defName, or null for what any age can
        /// build (a wall of wood, a bed, a door).</summary>
        public string? EraDefName { get; }

        public string? EraLabel { get; }

        /// <summary>Whether <see cref="MapCommands.PlaceBlueprint"/> accepts it now. False only while its research
        /// is unfinished; where it goes on the map is a separate, per-cell question.</summary>
        public bool CanPlace { get; }

        /// <summary>Why not, in the simulation's words, or null when <see cref="CanPlace"/>. The same sentence the
        /// command refuses with.</summary>
        public string? Reason { get; }

        /// <summary>Labels of the research still to finish before it can be placed, empty when none.</summary>
        public IReadOnlyList<string> ResearchNeeded { get; }

        /// <summary>The fixed materials, whatever it is built of (a storage hut's 25 logs).</summary>
        public IReadOnlyList<MaterialCost> Cost { get; }

        /// <summary>Units of the chosen stuff it takes on top of <see cref="Cost"/>, or 0 for a thing not made from
        /// stuff.</summary>
        public int StuffCount { get; }

        /// <summary>The stuffs it may be built of here and now, each with <see cref="StuffCount"/> as its count:
        /// what this age can make, or what the town holds. Empty for a thing not made from stuff.</summary>
        public IReadOnlyList<MaterialCost> Stuffs { get; }

        /// <summary>What it is built of when the command names no stuff, or null for a thing not made from
        /// stuff.</summary>
        public string? DefaultStuffDefName { get; }
    }

    /// <summary>One material and how many units of it, with how many the town holds now across the map and its
    /// stores.</summary>
    public readonly struct MaterialCost
    {
        internal MaterialCost(string defName, string label, int count, int held)
        {
            DefName = defName;
            Label = label;
            Count = count;
            Held = held;
        }

        public string DefName { get; }

        public string Label { get; }

        public int Count { get; }

        public int Held { get; }
    }
}
