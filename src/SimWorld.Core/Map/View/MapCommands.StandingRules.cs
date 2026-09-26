using System.Collections.Generic;
using System.Linq;

using SimWorld.Building;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The standing-rule half of <see cref="MapCommands"/>: <see cref="QueueBill"/>,
    /// <see cref="SetBillSuspended"/>, <see cref="SetStockpileFilter"/>, <see cref="UnmarkZone"/> and
    /// <see cref="SetHomeArea"/>. Its own file, per <c>docs/design/player-first.md</c> §3's own split —
    /// <see cref="MapCommands.PlaceBlueprint"/>/<see cref="MapCommands.MarkGrowingZone"/>/
    /// <see cref="MapCommands.MarkStockpile"/> in <c>MapCommands.cs</c> are the acts that <i>create</i>
    /// something; every method here is a rule the player <i>sets</i> against something that already exists —
    /// what to make, whether to keep making it, what to keep, where cleaning may go — and, once set, the
    /// simulation keeps deciding by it with no further input. Same result type, same named-outcome discipline,
    /// same "physically impossible only" refusal rule, same map resolution off
    /// <see cref="God.AttentionManager.FocusedSettlement"/> — see <see cref="MapCommands"/>'s own doc for all
    /// of that; this file only adds methods, it restates none of the reasoning already written there.
    ///
    /// <para/><b>The home area, made live for the first time.</b> <see cref="Building.AreaManager.Home"/> has
    /// existed since the Building module shipped, and <see cref="Filth.CleaningBounds.IsCleanable"/> has
    /// genuinely read it since the Filth module shipped — but nothing in <c>src/</c> ever wrote to it, so
    /// every read has always seen a permanently empty area and fallen through to
    /// <see cref="Filth.CleaningBounds"/>'s own room-based fallback. <see cref="SetHomeArea"/> is the first
    /// caller that ever sets a cell in it. That is a real behaviour change, not a plumbing exercise: the
    /// moment a player paints a home area, cleaning stops following the enclosed-room fallback and starts
    /// following the player's own paint exactly as <see cref="Filth.CleaningBounds"/>'s doc already promised
    /// it would — including a room the player deliberately paints out, which is accepted, not refused; see
    /// this class's own rule on unwise-but-legal below. <c>Filth/CleaningBounds.cs</c>'s own comment
    /// ("nothing populates the home area") is fixed alongside this, in that file, because it described a gap
    /// this method closes.
    ///
    /// <para/><b>The bill collision, decided.</b> <see cref="Crafting.CookingInitiative"/>,
    /// <see cref="Crafting.StonecutterInitiative"/> and <see cref="Crafting.GuildInitiative"/> each queue their
    /// own standing <see cref="Bill_Production"/> for a bench, and each already skips doing so when a bill for
    /// that same <see cref="RecipeDef"/> already sits on that bench — <c>HasBillFor</c>, present in all three,
    /// keyed on the recipe reference alone, with no notion of who queued the bill it finds. That is, by
    /// itself, exactly the "share ground rather than fight over it" rule <c>MapCommands.cs</c>'s own doc
    /// already sets for construction and zones (<see cref="Building.SettlementConstructionInitiative.CountBuiltOrPlanned"/>
    /// counts a player's Blueprint the same as its own; <see cref="Building.ZoneManager"/>'s one-zone-per-cell
    /// rule keeps the two from ever claiming the same ground): a player's bill for a recipe on a bench already
    /// answers the same need an initiative would have queued its own bill to answer, so <see cref="QueueBill"/>
    /// deliberately does <b>not</b> refuse, dedupe against, or otherwise special-case a bench that already
    /// carries a bill for the same recipe — RimWorld itself lets a bench carry several bills for one recipe,
    /// and refusing a second would be refusing something merely unwise, which this class never does.
    ///
    /// <para/>That reuse of <c>HasBillFor</c> is sound only because of one further choice, and it is the actual
    /// decision this file makes: <b><see cref="QueueBill"/> always queues its bill in
    /// <see cref="BillRepeatMode.Forever"/>, never <see cref="BillRepeatMode.RepeatCount"/>.</b>
    /// <c>HasBillFor</c> cannot tell "a bill that is still doing this job" from "a bill that used to and has
    /// gone quiet" — it only asks whether a bill for the recipe exists, not whether it can still fire. A
    /// finite bill (RimWorld's own default: "make 20 meals", <see cref="Bill_Production.repeatCount"/> counting
    /// down to zero) is a genuine one-time act, and once it is done it does not remove itself from the
    /// bench — <see cref="BillStack"/> has no such mechanism yet, and nothing here adds one. It would sit
    /// there forever afterwards, inert, and <c>HasBillFor</c> would keep reading it as "this recipe is
    /// covered" for the rest of the settlement's life: the moment research or supply later made that recipe
    /// genuinely worth an initiative's own bill, the initiative would still see the player's long-finished one
    /// and queue nothing. That is precisely the failure <c>docs/design/player-first.md</c> §5 names and
    /// forbids twice over: a one-time act would have <i>rewritten</i> the standing rule rather than merely
    /// interrupting it, and it would have done so by silently ceasing to enforce itself with nothing anywhere
    /// to say so. A <see cref="BillRepeatMode.Forever"/> bill has no such moment: it never goes quiet on its
    /// own (only <see cref="SetBillSuspended"/> — a further, explicit, reversible player act — stops it), so
    /// for as long as it sits on the bench it is telling <c>HasBillFor</c> the truth. A player who wants a
    /// one-off batch gets the same result RimWorld gives for "pause after this many": queue it, watch it run,
    /// then <see cref="SetBillSuspended"/> it — a second standing-rule call the player makes on purpose,
    /// instead of a repeatCount silently expiring behind their back.
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Queues a standing <see cref="Bill_Production"/> for <paramref name="recipeDefName"/> on the
        /// bill-giving bench standing at <paramref name="benchCell"/> — "make this here, from now on". Lands
        /// straight on <see cref="Things.CompBillGiver.BillStack"/> through <see cref="BillStack.AddBill"/>,
        /// the exact call <see cref="Crafting.CookingInitiative"/>/<see cref="Crafting.StonecutterInitiative"/>/
        /// <see cref="Crafting.GuildInitiative"/> already make for their own bills, so
        /// <see cref="Crafting.WorkGiver_DoBill"/> works a player's bill with no notion that it did not queue
        /// it itself. Always <see cref="BillRepeatMode.Forever"/> — see the class doc for why this is the one
        /// deliberate choice the whole bill-collision question turns on.
        ///
        /// <para/>Refuses only: no map open (<see cref="MapCommandOutcome.NoMap"/>); the cell off the map
        /// (<see cref="MapCommandOutcome.OffMap"/>); no bill-giving bench standing at the cell at all, or a
        /// recipe name that resolves to no <see cref="RecipeDef"/> (<see cref="MapCommandOutcome.Refused"/> and
        /// <see cref="MapCommandOutcome.UnknownDef"/> respectively); or a bench that exists but cannot run that
        /// recipe under any circumstances — it is not in the bench's own <see cref="Defs.ThingDef.AllRecipes"/>
        /// (<see cref="MapCommandOutcome.Refused"/>). <b>Never</b> refuses because nobody has researched the
        /// recipe yet or because no ingredient for it is anywhere on the map — a bill for something nobody can
        /// make yet is exactly the kind of unwise-but-legal decision this class exists to carry out; see the
        /// class doc on <see cref="MapCommands"/> itself.
        /// </summary>
        public static MapCommandResult QueueBill(IntVec3 benchCell, string recipeDefName)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            if (!GenGrid.InBounds(benchCell, map)) return MapCommandResult.OffMap(benchCell);

            CompBillGiver? bench = BillGiverAt(map, benchCell);
            if (bench == null) return MapCommandResult.Refused("No bill-giving bench stands at " + benchCell + ".");

            RecipeDef? recipe = ResolveRecipeDef(recipeDefName);
            if (recipe == null) return MapCommandResult.UnknownDef(recipeDefName);

            if (!bench.parent.def.AllRecipes.Contains(recipe))
            {
                return MapCommandResult.Refused(
                    bench.parent.def.LabelCap + " cannot run " + recipe.LabelCap + " at all.");
            }

            var bill = new Bill_Production(recipe) { repeatMode = BillRepeatMode.Forever };
            bench.BillStack.AddBill(bill);
            return MapCommandResult.Done(
                recipe.LabelCap + " queued at the " + bench.parent.def.LabelCap + " at " + benchCell + ".");
        }

        /// <summary>
        /// Suspends or resumes bill number <paramref name="billIndex"/> (0-based, in
        /// <see cref="BillStack"/> order) on the bench standing at <paramref name="benchCell"/> — the standing
        /// rule's own on/off switch, and the reversible half of the one-off-batch pattern the class doc
        /// describes for <see cref="QueueBill"/>. Sets <see cref="Bill.suspended"/> directly, which
        /// <see cref="Bill.ShouldDoNow"/> and <see cref="Bill.PawnAllowedToStartAnew"/> already respect — a
        /// suspended bill is skipped by <see cref="Crafting.WorkGiver_DoBill.FindBill"/> the very next time a
        /// pawn looks at the bench, and an un-suspended one is picked up the same way, with no other change
        /// needed anywhere.
        ///
        /// <para/>Refuses only: no map open; the cell off the map; no bill-giving bench at the cell; or
        /// <paramref name="billIndex"/> outside the bench's current bill count (all
        /// <see cref="MapCommandOutcome.Refused"/>, save the map/bounds cases). Never refuses for suspending a
        /// bill that would leave the settlement with nothing to eat, build with, or sell — that is the
        /// player's call, and a settlement that starves itself by pausing every bill on every bench is exactly
        /// the "bad decision must be allowed to be bad" case the class doc names.
        /// </summary>
        public static MapCommandResult SetBillSuspended(IntVec3 benchCell, int billIndex, bool suspended)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            if (!GenGrid.InBounds(benchCell, map)) return MapCommandResult.OffMap(benchCell);

            CompBillGiver? bench = BillGiverAt(map, benchCell);
            if (bench == null) return MapCommandResult.Refused("No bill-giving bench stands at " + benchCell + ".");

            IReadOnlyList<Bill> bills = bench.BillStack.Bills;
            if (billIndex < 0 || billIndex >= bills.Count)
            {
                return MapCommandResult.Refused(
                    "The bench at " + benchCell + " has no bill at index " + billIndex + ".");
            }

            bills[billIndex].suspended = suspended;
            return MapCommandResult.Done(
                "Bill " + billIndex + " at " + benchCell + (suspended ? " suspended." : " resumed."));
        }

        /// <summary>
        /// Replaces the storage filter of the <see cref="Zone_Stockpile"/> standing over
        /// <paramref name="anyCellInZone"/> with exactly <paramref name="allowedDefNames"/> — "only keep these
        /// here, from now on". Every stockpile on the map is already read by
        /// <c>AI.HaulAIUtility.TryFindBestStockpileCell</c> with no notion of who painted it (see
        /// <see cref="MapCommands"/>'s own doc on <see cref="MapCommands.MarkStockpile"/>), so a filter change
        /// here takes effect the moment the next hauling decision is made — no separate wiring needed.
        ///
        /// <para/>Refuses only: no map open; the cell off the map; or no <see cref="Zone_Stockpile"/> standing
        /// at the cell at all — a <see cref="Zone_Growing"/> there, or no zone at all, is
        /// <see cref="MapCommandOutcome.Refused"/>; and any name in <paramref name="allowedDefNames"/> that
        /// resolves to no <see cref="Defs.ThingDef"/> (<see cref="MapCommandOutcome.UnknownDef"/>), checked
        /// before the filter is touched so a bad name never leaves the stockpile half-changed. <b>Never</b>
        /// refuses for an empty <paramref name="allowedDefNames"/> — a stockpile that accepts nothing is a
        /// legal, if useless, stockpile, exactly as the class doc names it.
        /// </summary>
        public static MapCommandResult SetStockpileFilter(IntVec3 anyCellInZone, IReadOnlyList<string> allowedDefNames)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);
            if (!GenGrid.InBounds(anyCellInZone, map)) return MapCommandResult.OffMap(anyCellInZone);

            if (!(map.zoneManager.ZoneAt(anyCellInZone) is Zone_Stockpile stockpile))
            {
                return MapCommandResult.Refused("No stockpile stands at " + anyCellInZone + ".");
            }

            var resolved = new List<ThingDef>(allowedDefNames?.Count ?? 0);
            if (allowedDefNames != null)
            {
                for (int i = 0; i < allowedDefNames.Count; i++)
                {
                    ThingDef? def = ResolveThingDef(allowedDefNames[i]);
                    if (def == null) return MapCommandResult.UnknownDef(allowedDefNames[i]);
                    resolved.Add(def);
                }
            }

            stockpile.filter.SetDisallowAll();
            for (int i = 0; i < resolved.Count; i++) stockpile.filter.SetAllow(resolved[i], true);

            return MapCommandResult.Done(
                "Stockpile at " + anyCellInZone + " now allows " + resolved.Count + " kind(s) of goods.");
        }

        /// <summary>
        /// Removes <paramref name="cells"/> from whatever <see cref="Zone"/> currently holds each one — the
        /// undo for <see cref="MapCommands.MarkGrowingZone"/> and <see cref="MapCommands.MarkStockpile"/> alike,
        /// one call for either since <see cref="Building.ZoneManager"/> itself does not distinguish the two
        /// kinds when removing cells. A zone left with no cells at all is deregistered
        /// (<see cref="Building.ZoneManager.DeregisterZone"/>) rather than kept around empty; a zone that keeps
        /// some cells is left standing over what remains. Once a cell leaves a growing zone,
        /// <c>WorkGiver_GrowerSow</c> stops offering it as sowing work; once a cell leaves a stockpile,
        /// hauling stops targeting it — both read <see cref="Building.ZoneManager"/> fresh, so there is nothing
        /// else to notify.
        ///
        /// <para/>Refuses only: no map open; no cells given; any cell off the map (all-or-nothing over the
        /// whole batch, same reason <see cref="MapCommands.ValidateCells"/> is all-or-nothing for the marking
        /// commands). A cell that belongs to no zone is not a failure — <see cref="MapCommandOutcome.NoChange"/>
        /// when <b>none</b> of the given cells belonged to a zone, the same treatment
        /// <see cref="MapCommands.CancelDesignation"/> gives an empty cell.
        /// </summary>
        public static MapCommandResult UnmarkZone(IReadOnlyList<IntVec3> cells)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            var touchedZones = new List<Zone>();
            int removed = 0;
            for (int i = 0; i < cells!.Count; i++)
            {
                Zone? zone = map.zoneManager.ZoneAt(cells[i]);
                if (zone == null) continue;
                map.zoneManager.RemoveCell(zone, cells[i]);
                removed++;
                if (!touchedZones.Contains(zone)) touchedZones.Add(zone);
            }

            if (removed == 0) return MapCommandResult.NoChange("None of the given cells belong to a zone.");

            for (int i = 0; i < touchedZones.Count; i++)
            {
                if (touchedZones[i].CellCount == 0) map.zoneManager.DeregisterZone(touchedZones[i]);
            }

            return MapCommandResult.Done("Unmarked " + removed + " cell(s) from " + touchedZones.Count + " zone(s).");
        }

        /// <summary>
        /// Adds or removes <paramref name="cells"/> from <see cref="Building.AreaManager.Home"/> — "clean and
        /// tidy here, from now on" (or not). See the class doc for why this is the first call in the whole
        /// codebase that ever writes to the home area, and what that changes for
        /// <see cref="Filth.CleaningBounds"/>. It is also "build here". Once any cell is painted,
        /// <see cref="Building.SettlementConstructionInitiative"/> places everything the settlement decides it
        /// needs inside the home area, and nothing outside it.
        ///
        /// <para/>Refuses only: no map open; no cells given; any cell off the map. <b>Never</b> refuses a home
        /// area that excludes the player's whole settlement, or one that leaves a single filthy room outside
        /// it on purpose — <see cref="Filth.CleaningBounds"/>'s own doc already accepts a home area "that
        /// deliberately excludes a room somebody does not want cleaned", and this is the method that lets a
        /// player actually make that choice.
        /// </summary>
        public static MapCommandResult SetHomeArea(IReadOnlyList<IntVec3> cells, bool included)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            MapCommandResult? boundsBad = ValidateCellsInBounds(cells, map);
            if (boundsBad != null) return boundsBad;

            for (int i = 0; i < cells!.Count; i++)
            {
                map.areaManager.Home[cells[i]] = included;
            }

            return MapCommandResult.Done(
                (included ? "Added " : "Removed ") + cells.Count + " cell(s) "
                + (included ? "to" : "from") + " the home area.");
        }

        /// <summary>
        /// Sets the settlement's own default <see cref="Health.MedicalCareCategory"/> — "tend like this, from
        /// now on" (task #104's own lever; RimWorld's real one is a per-pawn dropdown,
        /// <c>Pawn_PlayerSettings.medCare</c>, that this god-game has no pawn-level UI to expose, so the whole
        /// settlement carries one value instead, read by <see cref="AI.WorkGiver_Tend"/> and
        /// <see cref="Health.MedicineUtility.FindBestMedicine"/> the moment either next runs — no other wiring
        /// needed, the same "write the field, the simulation notices on its own" shape <see cref="SetHomeArea"/>
        /// and <see cref="SetStockpileFilter"/> already use).
        ///
        /// <para/>Refuses only: no map open (<see cref="MapCommandOutcome.NoMap"/>). <b>Never</b> refuses
        /// <see cref="Health.MedicalCareCategory.NoCare"/> — a settlement that stops tending its own wounded is
        /// exactly the unwise-but-legal decision this class's whole doc names, not something to protect the
        /// player from; nor <see cref="Health.MedicalCareCategory.Best"/> with no medicine anywhere on the map
        /// — a wish this settlement cannot yet grant is not the same as an impossible one.
        /// </summary>
        public static MapCommandResult SetMedicalCare(Health.MedicalCareCategory category)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            map.medicalCare = category;
            return MapCommandResult.Done("Medical care set to " + category + ".");
        }

        // -----------------------------------------------------------------------------------------------
        // Resolution — additions for this file only. ResolveMap/ResolveThingDef/ValidateCells already live
        // in MapCommands.cs and are shared across the partial class without repeating them here.
        // -----------------------------------------------------------------------------------------------

        /// <summary>The first bill-giving bench standing at <paramref name="cell"/>, or null. Mirrors
        /// <see cref="Crafting.WorkGiver_DoBill.BillGiverFor"/>'s own "does this Thing carry the comp"
        /// check, applied to a cell instead of a candidate Thing already in hand.</summary>
        private static CompBillGiver? BillGiverAt(SimWorld.Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i] is ThingWithComps twc)
                {
                    CompBillGiver? comp = twc.GetComp<CompBillGiver>();
                    if (comp != null) return comp;
                }
            }
            return null;
        }

        private static RecipeDef? ResolveRecipeDef(string? defName) =>
            string.IsNullOrEmpty(defName) ? null : DefDatabase<RecipeDef>.GetNamedSilentFail(defName);

        /// <summary>Bounds only, over the whole batch before any cell is touched — <see cref="UnmarkZone"/> and
        /// <see cref="SetHomeArea"/> both want this without <see cref="MapCommands.ValidateCells"/>'s extra
        /// one-zone-per-cell refusal, which does not apply to either (removing a cell from a zone, or painting
        /// an area that can overlap a zone freely by design — see <see cref="Building.Area"/>'s own doc).</summary>
        private static MapCommandResult? ValidateCellsInBounds(IReadOnlyList<IntVec3>? cells, SimWorld.Map.Map map)
        {
            if (cells == null || cells.Count == 0) return MapCommandResult.Refused("No cells given.");
            for (int i = 0; i < cells.Count; i++)
            {
                if (!GenGrid.InBounds(cells[i], map)) return MapCommandResult.OffMap(cells[i]);
            }
            return null;
        }
    }
}
