using SimWorld.AI;
using SimWorld.Building;
using SimWorld.Defs;

namespace SimWorld.Map.View
{
    /// <summary>
    /// The change-what-a-field-grows half of <see cref="MapCommands"/>: <see cref="SetZonePlant"/>
    /// (RimWorld: <c>Verse.Command_SetPlantToGrow</c>, the gizmo on a selected growing zone). Its own file, the
    /// same split <c>MapCommands.Roofs.cs</c> and <c>MapCommands.StandingRules.cs</c> keep — see
    /// <see cref="MapCommands"/> for the shared discipline (result type, named outcomes, map resolution off
    /// <see cref="God.AttentionManager.FocusedSettlement"/>).
    ///
    /// <para/><b>Why it is needed.</b> <see cref="MapCommands.MarkGrowingZone"/> lets the player paint a new
    /// zone with a crop, but a zone that already stands — above all the settlement's own field, which was
    /// painted before the player looked — could not be told to grow something else. And the settlement now
    /// grows its own herb garden (<see cref="FarmingInitiative.RunHerbGarden"/>), which the player may want
    /// swapped for something else.
    /// </summary>
    public static partial class MapCommands
    {
        /// <summary>
        /// Tells the <see cref="Zone_Growing"/> standing over <paramref name="anyCellInZone"/> to grow
        /// <paramref name="cropDefName"/> from now on — "grow this here instead". Plants already standing in
        /// the zone are not touched; citizens cut the wrong ones out as they already do
        /// (<c>AI.WorkGiver_PlantsCut</c>) and sow the new crop in their place.
        ///
        /// <para/>Refuses only: no map open (<see cref="MapCommandOutcome.NoMap"/>); the cell off the map
        /// (<see cref="MapCommandOutcome.OffMap"/>); no such def (<see cref="MapCommandOutcome.UnknownDef"/>);
        /// a def with no <see cref="PlantProperties"/>, which cannot be grown at all
        /// (<see cref="MapCommandOutcome.Refused"/>); no growing zone at the cell
        /// (<see cref="MapCommandOutcome.Refused"/>); and <b>a crop nobody in the settlement can sow</b>
        /// (<see cref="MapCommandOutcome.Refused"/>) — see below. Naming the plant the zone already grows is
        /// <see cref="MapCommandOutcome.NoChange"/>, not a failure.
        ///
        /// <para/><b>The one refusal that is not "unwise, so allow it", and where it comes from.</b> A plant
        /// that sets <see cref="PlantProperties.sowMinSkill"/> (healroot asks for Plants 8) is sown by nobody
        /// below it, so choosing it for a zone no living, standing citizen with Growing work on can sow leaves
        /// the whole zone unsown for as long as that stays true. RimWorld's
        /// <c>Command_SetPlantToGrow.WarnAsAppropriate</c> asks exactly this question
        /// (<c>FreeColonistsSpawned</c>, Plants level at least <c>sowMinSkill</c>, not downed, Growing active)
        /// — but <b>it applies the choice first and then shows a message box</b> ("NoGrowerCanPlant"). A
        /// command result has no modal to show after the fact: a <see cref="MapCommandOutcome.Done"/> with a
        /// warning in its <see cref="MapCommandResult.Reason"/> is a success a host is free to present as one,
        /// and a field that was feeding people would have been turned into one that feeds nobody. So this
        /// carries the question to a refusal that says why, and the zone keeps what it had. It asks the same
        /// question the settlement asks itself before painting a garden
        /// (<see cref="FarmingInitiative.AnyoneCanSow"/>), so the two can never disagree about who counts. A
        /// recorded translation of RimWorld's warning, not an expression of the "never second-guess the
        /// player" rule above it: nothing here judges whether the crop is a good one, only whether anyone
        /// could ever carry the order out.
        ///
        /// <para/><b>The settlement's own zones are handed over, not fought over.</b>
        /// <see cref="FarmingInitiative"/> finds its food field and its herb garden by label and puts its own
        /// crop back on them every pass, so a plant set on one of those would be quietly undone within a
        /// minute. Changing the plant of one therefore adopts it: it takes the label a zone the player
        /// painted carries (<see cref="MarkGrowingZone"/>'s) and is sowing again if the settlement had
        /// paused it. The settlement then lays out its own field or garden afresh if it still needs one —
        /// beside the player's, exactly as <see cref="MapCommands"/> already records for a field the player
        /// painted. Nothing is stored to remember the choice: the label is the whole of it.
        /// </summary>
        public static MapCommandResult SetZonePlant(IntVec3 anyCellInZone, string cropDefName)
        {
            SimWorld.Map.Map? map = ResolveMap(out string? mapReason);
            if (map == null) return MapCommandResult.NoMap(mapReason!);

            if (!GenGrid.InBounds(anyCellInZone, map)) return MapCommandResult.OffMap(anyCellInZone);

            ThingDef? crop = ResolveThingDef(cropDefName);
            if (crop == null) return MapCommandResult.UnknownDef(cropDefName);
            if (crop.plant == null)
            {
                return MapCommandResult.Refused(crop.LabelCap + " cannot be grown — it has no plant properties.");
            }

            if (!(map.zoneManager.ZoneAt(anyCellInZone) is Zone_Growing zone))
            {
                return MapCommandResult.Refused("No growing zone stands at " + anyCellInZone + ".");
            }

            if (zone.plantDefToGrow == crop)
            {
                return MapCommandResult.NoChange("The zone at " + anyCellInZone + " already grows " + crop.LabelCap + ".");
            }

            if (crop.plant.sowMinSkill > 0
                && !FarmingInitiative.AnyoneCanSow(HuntingInitiative.SettlementFor(map), map, crop))
            {
                return MapCommandResult.Refused(
                    "No citizen can sow " + crop.LabelCap + " — it needs Plants skill " + crop.plant.sowMinSkill
                    + ", and nobody who is up and working at Growing has it.");
            }

            zone.plantDefToGrow = crop;

            bool adopted = zone.label == FarmingInitiative.FieldLabel || zone.label == FarmingInitiative.HerbGardenLabel;
            if (adopted)
            {
                zone.label = new Zone_Growing().label;
                zone.allowSow = true;
            }

            return MapCommandResult.Done(
                "The zone at " + anyCellInZone + " now grows " + crop.LabelCap + " (" + zone.CellCount + " cell(s))."
                + (adopted ? " It is yours now; the settlement will lay out its own if it still needs one." : ""));
        }
    }
}
