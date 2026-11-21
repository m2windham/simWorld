using System.Collections.Generic;

using SimWorld.Crafting;
using SimWorld.Health;

namespace SimWorld.Map.View
{
    /// <summary>
    /// What every standing-rule and order lever on <see cref="MapCommands"/> is currently set to, on one map at
    /// one tick (REQ 005). Without it each of those commands is write-only: the player can set a rule and cannot
    /// see what it is set to.
    ///
    /// <para/><b>Why its own capture rather than more fields on <see cref="MapViewSnapshot"/>.</b> The snapshot
    /// is pulled whole once and then by <see cref="MapViewSnapshot.CaptureChanges(MapViewVersions?)"/> every
    /// frame; these values change only when the player (or a settlement initiative) acts, and two of them are a
    /// cell mask each. Carrying them on the per-frame path would pay for 80,000 bools a frame to report nothing
    /// new. So a host reads this with <see cref="MapViewSnapshot.CaptureLevers()"/> when a panel opens and after
    /// every command it sends — the same "capture, render, discard" rule, on a slower clock.
    ///
    /// <para/>Read-only, like the rest of the namespace: every handle is a defName or an id, nothing here holds
    /// a Def or a live object, and the only way to change any value below is the matching
    /// <see cref="MapCommands"/> method.
    /// </summary>
    public sealed class SettlementLevers
    {
        internal SettlementLevers(
            bool hasMap,
            string? absenceReason,
            int ticksGame,
            IReadOnlyList<BenchView> benches,
            IReadOnlyList<ZoneView> zones,
            AreaMask home,
            AreaMask snowClear,
            MedicalCareCategory medicalCare,
            IReadOnlyList<CitizenOrdersView> citizens)
        {
            HasMap = hasMap;
            AbsenceReason = absenceReason;
            TicksGame = ticksGame;
            Benches = benches;
            Zones = zones;
            Home = home;
            SnowClear = snowClear;
            MedicalCare = medicalCare;
            Citizens = citizens;
        }

        /// <summary>False when no interior is open; <see cref="AbsenceReason"/> then says why, and every list
        /// below is empty.</summary>
        public bool HasMap { get; }

        public string? AbsenceReason { get; }

        public int TicksGame { get; }

        /// <summary>REQ 005 items 1 and 2: every spawned bill-giving bench, its bills in <c>BillStack</c> order,
        /// and what it can run. In the map's own Thing order.</summary>
        public IReadOnlyList<BenchView> Benches { get; }

        /// <summary>REQ 005 items 3 and 4: every zone, stockpiles with their filter.</summary>
        public IReadOnlyList<ZoneView> Zones { get; }

        /// <summary>REQ 005 item 5: what <see cref="MapCommands.SetHomeArea"/> painted.</summary>
        public AreaMask Home { get; }

        /// <summary>REQ 005 item 5: what <see cref="MapCommands.SetSnowClearArea"/> painted.</summary>
        public AreaMask SnowClear { get; }

        /// <summary>REQ 005 item 6: the value <see cref="MapCommands.SetMedicalCare"/> last set (the map's
        /// default, <see cref="MedicalCareCategory.Best"/>, until then).</summary>
        public MedicalCareCategory MedicalCare { get; }

        /// <summary>REQ 005 item 7: every spawned humanlike pawn and whether they are under the player's
        /// orders, keyed by the same id <see cref="PawnView.ThingId"/> carries.</summary>
        public IReadOnlyList<CitizenOrdersView> Citizens { get; }
    }

    /// <summary>A defName with the label a player reads for it (see <see cref="DefLabels"/>).</summary>
    public readonly struct DefOption
    {
        internal DefOption(string defName, string label)
        {
            DefName = defName;
            Label = label;
        }

        public string DefName { get; }

        public string Label { get; }
    }

    /// <summary>One recipe a placed bench can run, with whether its research is finished. Information, not a
    /// refusal: <see cref="MapCommands.QueueBill"/> accepts an unresearched recipe, and the bill simply waits
    /// (<c>RecipeDef.AvailableNow</c>).</summary>
    public readonly struct RecipeOption
    {
        internal RecipeOption(string defName, string label, bool researched)
        {
            DefName = defName;
            Label = label;
            Researched = researched;
        }

        public string DefName { get; }

        public string Label { get; }

        public bool Researched { get; }
    }

    /// <summary>One spawned bill-giving bench: where it stands (the cell <see cref="MapCommands.QueueBill"/>
    /// and <see cref="MapCommands.SetBillSuspended"/> take), its bills, and its recipes.</summary>
    public sealed class BenchView
    {
        internal BenchView(
            int thingId,
            string defName,
            string label,
            IntVec3 position,
            IReadOnlyList<BillView> bills,
            IReadOnlyList<RecipeOption> recipes)
        {
            ThingId = thingId;
            DefName = defName;
            Label = label;
            Position = position;
            Bills = bills;
            Recipes = recipes;
        }

        public int ThingId { get; }

        public string DefName { get; }

        public string Label { get; }

        /// <summary>The bench's own position: the <c>benchCell</c> the bill commands take.</summary>
        public IntVec3 Position { get; }

        /// <summary>In <c>BillStack</c> order, so <see cref="BillView.Index"/> is the <c>billIndex</c>
        /// <see cref="MapCommands.SetBillSuspended"/> takes.</summary>
        public IReadOnlyList<BillView> Bills { get; }

        /// <summary><c>ThingDef.AllRecipes</c> of this bench, in that order — exactly what
        /// <see cref="MapCommands.QueueBill"/> accepts here.</summary>
        public IReadOnlyList<RecipeOption> Recipes { get; }
    }

    /// <summary>
    /// One bill on a bench. Who queued it is not recorded anywhere in the simulation (<c>Bill</c> has no
    /// origin field), so it is not reported; a settlement initiative's bill is the one in
    /// <see cref="BillRepeatMode.TargetCount"/> mode, since <see cref="MapCommands.QueueBill"/> only ever queues
    /// <see cref="BillRepeatMode.Forever"/> — a convention a host may show, not a guarantee.
    /// </summary>
    public sealed class BillView
    {
        internal BillView(
            int index,
            string recipeDefName,
            string recipeLabel,
            bool suspended,
            BillRepeatMode? repeatMode,
            int? repeatCountLeft,
            int? targetCount,
            bool paused)
        {
            Index = index;
            RecipeDefName = recipeDefName;
            RecipeLabel = recipeLabel;
            Suspended = suspended;
            RepeatMode = repeatMode;
            RepeatCountLeft = repeatCountLeft;
            TargetCount = targetCount;
            Paused = paused;
        }

        /// <summary>Position in the bench's <c>BillStack</c>; the <c>billIndex</c> for
        /// <see cref="MapCommands.SetBillSuspended"/>.</summary>
        public int Index { get; }

        public string RecipeDefName { get; }

        public string RecipeLabel { get; }

        /// <summary>The flag <see cref="MapCommands.SetBillSuspended"/> sets.</summary>
        public bool Suspended { get; }

        /// <summary>Null for a bill that is not a production bill (surgery), which has no repeat mode.</summary>
        public BillRepeatMode? RepeatMode { get; }

        /// <summary>Iterations still to make, for <see cref="BillRepeatMode.RepeatCount"/> only; null
        /// otherwise.</summary>
        public int? RepeatCountLeft { get; }

        /// <summary>The stock level a <see cref="BillRepeatMode.TargetCount"/> bill makes up to; null
        /// otherwise.</summary>
        public int? TargetCount { get; }

        /// <summary>A <see cref="BillRepeatMode.TargetCount"/> bill that reached its target and is waiting for
        /// stock to fall, as of the last time a crafter checked it. Never set by the player; false for other
        /// modes. (<c>Bill.ShouldDoNow</c> is deliberately not called to report a fresher answer: on a
        /// TargetCount bill it rewrites this very flag, and a read must not change play.)</summary>
        public bool Paused { get; }
    }

    /// <summary>The two kinds of zone the commands paint.</summary>
    public enum ZoneKind
    {
        Stockpile,
        Growing,
    }

    /// <summary>
    /// One zone and its cells.
    ///
    /// <para/><b><see cref="Id"/> is good for this capture only.</b> The simulation gives zones no identity
    /// beyond the object, so the id is the zone's position in the map's zone list, which shifts when a zone
    /// is removed. Commands name a zone by any of its cells (<see cref="AnyCell"/>), never by this id — use the
    /// id to group, the cell to act.
    /// </summary>
    public sealed class ZoneView
    {
        internal ZoneView(
            int id,
            ZoneKind kind,
            string label,
            IReadOnlyList<IntVec3> cells,
            IReadOnlyList<DefOption> allowed,
            string? plantDefToGrow,
            bool allowSow)
        {
            Id = id;
            Kind = kind;
            Label = label;
            Cells = cells;
            Allowed = allowed;
            PlantDefToGrow = plantDefToGrow;
            AllowSow = allowSow;
        }

        public int Id { get; }

        public ZoneKind Kind { get; }

        /// <summary>The zone's own label ("Stockpile", "Growing zone", or a name a settlement gave it such as
        /// its granary).</summary>
        public string Label { get; }

        public IReadOnlyList<IntVec3> Cells { get; }

        /// <summary>A cell of this zone — the handle <see cref="MapCommands.SetStockpileFilter"/> takes.</summary>
        public IntVec3 AnyCell => Cells.Count > 0 ? Cells[0] : IntVec3.Invalid;

        /// <summary>
        /// For a stockpile, the storable things its filter allows, ordered by defName; empty for a growing zone.
        /// Restricted to <see cref="LeverCatalogue.StorableThings"/> — a filter built from the default preset
        /// also "allows" every building, plant and race def, none of which can ever lie in a stockpile, and
        /// listing them would bury the answer. A <see cref="MapCommands.SetStockpileFilter"/> call naming only
        /// storable things reads back exactly.
        /// </summary>
        public IReadOnlyList<DefOption> Allowed { get; }

        /// <summary>For a growing zone, the crop it sows; null for a stockpile.</summary>
        public string? PlantDefToGrow { get; }

        /// <summary>For a growing zone, whether sowing is on; false for a stockpile.</summary>
        public bool AllowSow { get; }
    }

    /// <summary>One painted area as a bit per cell, row-major like <see cref="TerrainLayer.Cells"/>.</summary>
    public sealed class AreaMask
    {
        internal AreaMask(string label, int sizeX, int sizeZ, IReadOnlyList<bool> cells, int trueCount)
        {
            Label = label;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Cells = cells;
            TrueCount = trueCount;
        }

        public string Label { get; }

        public int SizeX { get; }

        public int SizeZ { get; }

        /// <summary>Cell (x, z) is at <c>z * SizeX + x</c>.</summary>
        public IReadOnlyList<bool> Cells { get; }

        /// <summary>How many cells are painted.</summary>
        public int TrueCount { get; }

        public bool Contains(int x, int z) => Cells[z * SizeX + x];
    }

    /// <summary>Whether one person is under the player's orders: what <see cref="MapCommands.CancelOrder"/>
    /// would cancel.</summary>
    public sealed class CitizenOrdersView
    {
        internal CitizenOrdersView(
            int thingId, string name, string? currentJobDefName, bool onPlayerOrder, int queuedOrders)
        {
            ThingId = thingId;
            Name = name;
            CurrentJobDefName = currentJobDefName;
            OnPlayerOrder = onPlayerOrder;
            QueuedOrders = queuedOrders;
        }

        public int ThingId { get; }

        public string Name { get; }

        public string? CurrentJobDefName { get; }

        /// <summary>The job in hand is one the player ordered (<c>Job.playerForced</c>).</summary>
        public bool OnPlayerOrder { get; }

        /// <summary>Player orders still waiting in the queue.</summary>
        public int QueuedOrders { get; }

        /// <summary>Whether <see cref="MapCommands.CancelOrder"/> would find anything to cancel.</summary>
        public bool HasOrders => OnPlayerOrder || QueuedOrders > 0;
    }

    /// <summary>What an orderable job's target is: the overload of <see cref="MapCommands.OrderJob(int, string, IntVec3)"/>
    /// to call.</summary>
    public enum OrderTargetKind
    {
        Cell,
        Thing,
    }

    /// <summary>A job meant to be ordered directly, with the kind of target it takes and what that target
    /// should be, in words.</summary>
    public readonly struct OrderableJob
    {
        internal OrderableJob(string defName, string label, OrderTargetKind target, string targetHint)
        {
            DefName = defName;
            Label = label;
            Target = target;
            TargetHint = targetHint;
        }

        public string DefName { get; }

        public string Label { get; }

        public OrderTargetKind Target { get; }

        /// <summary>What to point it at ("a mineable rock", "a wounded person"). Advice for the picker only:
        /// <see cref="MapCommands.OrderJob(int, string, int)"/> never refuses a wrong kind of thing.</summary>
        public string TargetHint { get; }
    }

    /// <summary>One bill-giving bench def and every recipe it can run; see
    /// <see cref="LeverCatalogue.BillGivers"/>.</summary>
    public sealed class BenchRecipes
    {
        internal BenchRecipes(string benchDefName, string benchLabel, IReadOnlyList<DefOption> recipes)
        {
            BenchDefName = benchDefName;
            BenchLabel = benchLabel;
            Recipes = recipes;
        }

        public string BenchDefName { get; }

        public string BenchLabel { get; }

        public IReadOnlyList<DefOption> Recipes { get; }
    }
}
