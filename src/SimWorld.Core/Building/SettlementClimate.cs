using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Health;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Weather;
using SimWorld.World;

namespace SimWorld.Building
{
    /// <summary>What the weather is doing to a settlement today, read off its climate rather than off anybody's
    /// discomfort.</summary>
    public readonly struct ClimateOutlook
    {
        /// <summary>The day's mean outdoor temperature: the tile's annual mean, the season, the weather and every
        /// active condition (a heat wave, a cold snap), without the sun's daily swing.</summary>
        public readonly float DailyMean;

        public ClimateOutlook(float dailyMean)
        {
            DailyMean = dailyMean;
        }

        /// <summary>The day's high and low: the mean plus or minus RimWorld's daily swing
        /// (<see cref="TemperatureTuning.DailyTempVariationAmplitude"/>).</summary>
        public float DailyHigh => DailyMean + TemperatureTuning.DailyTempVariationAmplitude;

        public float DailyLow => DailyMean - TemperatureTuning.DailyTempVariationAmplitude;
    }

    /// <summary>
    /// Builds a house's hearth or cooler when the season calls for it and has it switched on and off
    /// (shelter lane; translation: the settlement is the player). RimWorld's player builds a campfire or a passive
    /// cooler in a room, refuels it, and flicks it off when it is not wanted; this is that player's judgement, made
    /// for every finished house that has a household and a spot kept for the device (<see cref="DwellingPartKind"/>).
    /// <para/>
    /// <b>The decision reads the weather, and then the people.</b> A room left alone settles toward the outdoor day's
    /// mean within hours (RimWorld's room model, <see cref="RoomTracker"/>), so the question is where today's mean
    /// stands against what the household finds comfortable: above the warmest member's comfortable maximum it
    /// wants cooling, below the coldest member's comfortable minimum it wants heat, and in between neither. The
    /// mean is the climate's annual mean, the season (<see cref="GenTemperature.OffsetFromSeasonCycle"/>), the
    /// weather and every active condition, the sum <c>WeatherManager</c> feeds the map without the sun's swing.
    /// So a heat wave turns the coolers on the day it begins, a warm afternoon does not, and a cooler built for
    /// summer stops drinking wood when autumn comes. The hearth is built on the same reading, but it burns by the
    /// people (<see cref="SomeoneHomeIsCold"/>): lit when somebody at home is colder than they find comfortable,
    /// whatever the mean, and kept burning until they are warm with a margin. RimWorld's player sets a heater's
    /// target; here the settlement sets it by the most vulnerable person in the house, which in a house with a
    /// baby is the baby. A fire burning at its 28 C ceiling all winter in an empty house ate the wood a baby's
    /// hearth needed; a fire that answers the room burns what the room needs.
    /// <para/>
    /// <b>The era is in what can be built.</b> A hearth (<see cref="ShelterThingDefOf.Campfire"/>) needs Fire, a
    /// passive cooler Windcatchers, and only a house with a spot for one gets one: the earth-sheltered rungs keep
    /// no cooler spot because they need none. A device is built only when it is first wanted, so a settlement in a
    /// mild climate never pays for coolers.
    /// <para/>
    /// Nothing here draws from <see cref="Rand"/>.
    /// </summary>
    public static class SettlementClimate
    {
        /// <summary>Today's outlook on <paramref name="map"/>. A map with no world tile has no season; its outlook
        /// is whatever its outdoor air is now.</summary>
        public static ClimateOutlook Outlook(Map.Map map)
        {
            MapClimate? climate = map.weatherManager?.Climate;
            if (climate == null) return new ClimateOutlook(map.outdoorTemperature);
            float mean = climate.averageTemperature
                + GenTemperature.OffsetFromSeasonCycle(Find.TickManager.TicksAbs, climate.latitude, climate.longitude)
                + map.weatherManager!.TemperatureOffset
                + map.gameConditionManager.AggregateTemperatureOffset();
            return new ClimateOutlook(mean);
        }

        /// <summary>Builds and switches every house's devices on <paramref name="map"/>; returns how many
        /// blueprints it placed.</summary>
        public static int Tick(Settlement settlement, Map.Map map)
        {
            ClimateOutlook outlook = Outlook(map);
            int placed = TendDormitory(settlement, map, outlook);
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            if (dwellings.Count == 0) return placed;
            for (int i = 0; i < dwellings.Count; i++)
            {
                Dwelling d = dwellings[i];
                if (d.householdId == Pawn_RelationsTracker.None || !d.IsComplete(map)) continue;
                if (!TryComfortOf(settlement, d.householdId, out FloatRange comfy)) continue;
                // The season says a hearth will be needed, and it is built; whether it burns is the thermostat's.
                bool coldSeason = outlook.DailyMean < comfy.min;
                bool someoneCold = SomeoneHomeIsCold(settlement, map, d, HearthWanted(map, d) ? HearthWarmthMargin : 0f);
                bool wantCool = outlook.DailyMean > comfy.max;
                placed += Tend(map, d, DwellingPartKind.HearthSpot, ShelterThingDefOf.Campfire, someoneCold, coldSeason || someoneCold);
                placed += Tend(map, d, DwellingPartKind.CoolerSpot, ShelterThingDefOf.PassiveCooler, wantCool, wantCool);
            }
            return placed;
        }

        /// <summary>
        /// How far above a member's comfortable minimum a lit hearth keeps burning before it is let go out: the
        /// thermostat's dead band, so a fire is not lit and doused every few hours by somebody walking over to flick
        /// it. Five degrees is the gap between RimWorld's default heater target (21 C, <c>CompProperties_TempControl
        /// .defaultTargetTemperature</c>, recalled, not re-read) and a naked human's comfortable minimum (16 C, the
        /// shipped <c>ComfyTemperatureMin</c>): a baby's hearth runs to RimWorld's own setting.
        /// </summary>
        public const float HearthWarmthMargin = 5f;

        /// <summary>
        /// Whether a member of <paramref name="dwelling"/>'s household is at home in air colder than they find
        /// comfortable, plus <paramref name="margin"/> (RimWorld: the heater's thermostat, <c>CompTempControl</c>, whose
        /// target the player sets; here the settlement sets it, from whoever is most vulnerable in the house).
        /// <para/>
        /// <b>Why the people and not only the weather.</b> The season's mean says when a robed adult needs a fire,
        /// and that was the whole of the decision: a household lit its hearth when the day's mean fell under its
        /// coldest-blooded member's comfortable minimum. A day can average above that and still drop seven degrees
        /// overnight (<see cref="TemperatureTuning.DailyTempVariationAmplitude"/>), and a baby, comfortable only from
        /// 16 C, wearing nothing and unable to walk to warmth, takes hypothermia it cannot shed, because it heals
        /// only in comfortable air. So the hearth answers the room: lit when somebody at home is below
        /// comfort, and kept lit until they are <see cref="HearthWarmthMargin"/> above it. Somebody away from home
        /// is not counted, since the hearth cannot warm them and an empty house burns no wood, unless the cold has
        /// already hurt them (<see cref="FreezingAway"/>, exposure3 lane): then the house is warmed for them to come
        /// home to. Before, a house stood at -5 C because nobody was in it, and nobody went in because it stood at
        /// -5 C; the 600-day 40/201 arc lost ten children in one frost that way, out of doors or asleep in the
        /// dormitory. Somebody of another household lying in one of the house's beds is counted (exposure3 lane): a baby with no house of its own,
        /// brought in from the cold (<see cref="AI.BabySafetyUtility"/>), is the house's most vulnerable person.
        /// </summary>
        internal static bool SomeoneHomeIsCold(Settlement settlement, Map.Map map, Dwelling dwelling, float margin)
        {
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead || !p.Spawned || p.Map != map) continue;
                if (!dwelling.InteriorContains(p.Position))
                {
                    if (p.relations.familyId == dwelling.householdId && FreezingAway(p)) return true;
                    continue;
                }
                if (p.relations.familyId != dwelling.householdId && !LiesInBedOf(p, dwelling)) continue;
                float comfyMin = TemperatureInjuryUtility.ComfortableTemperatureRange(p).min;
                if (TemperatureInjuryUtility.AmbientTemperature(p) < comfyMin + margin) return true;
            }
            return false;
        }

        /// <summary>A member away from home whose hypothermia has reached the minor stage: the hearth is lit for them to
        /// come home to, as RimWorld's heater keeps a room warm for whoever needs it, so the search for warmth
        /// (<see cref="AI.JobGiver_SeekSafeTemperature"/>, at the serious stage) has somewhere to go.</summary>
        private static bool FreezingAway(Pawn p) =>
            p.health.hediffSet.GetFirstHediffOfDef(TemperatureDefOf.Hypothermia) is Hediff cold
            && cold.CurStageIndex >= (int)TemperatureInjuryStage.Minor;

        /// <summary>Somebody of another household laid in one of <paramref name="dwelling"/>'s beds: a baby brought in
        /// from the cold (<see cref="AI.BabySafetyUtility"/>). The hearth burns for the most vulnerable person lying in
        /// the house, as RimWorld's heater warms whoever is in its room.</summary>
        private static bool LiesInBedOf(Pawn p, Dwelling dwelling) =>
            AI.RestUtility.CurrentBed(p) is Thing bed && dwelling.InteriorContains(bed.Position);

        /// <summary>
        /// Whether <paramref name="bed"/> stands in a room this settlement heats for whoever lies in it, so it is safe
        /// to lie down in however cold the air is now: a finished, lived-in house whose hearth has fuel (its
        /// thermostat counts anybody lying in its beds, <see cref="SomeoneHomeIsCold"/>). A house a quake has opened is
        /// not one: its hearth warms the outdoors. Nor is the dormitory, though its hearth is tended the same way
        /// (<see cref="TendDormitory"/>): one campfire does not hold forty beds' worth of room at a baby's comfort in a
        /// hard frost (exposure4 lane: given that promise, two babies with no house were carried there at -20 C outside
        /// and froze at 5 C indoors). A dormitory bed is safe when its air is, as RimWorld judges every bed.
        /// </summary>
        public static bool WillWarm(Map.Map map, Thing bed)
        {
            Dwelling? house = DwellingUtility.DwellingOfBed(bed);
            return house != null && !house.beingDemolished && house.householdId != Pawn_RelationsTracker.None
                && house.IsComplete(map) && HasFueledHearth(map, house);
        }

        /// <summary>Whether <paramref name="dwelling"/> has a hearth standing with fuel in its tank: one its thermostat
        /// can light (<see cref="SomeoneHomeIsCold"/>).</summary>
        public static bool HasFueledHearth(Map.Map map, Dwelling dwelling)
        {
            foreach (IntVec3 spot in dwelling.Spots(DwellingPartKind.HearthSpot))
            {
                Thing? device = DeviceAt(map, spot, ShelterThingDefOf.Campfire);
                if ((device as ThingWithComps)?.GetComp<CompRefuelable>()?.HasFuel == true) return true;
            }
            return false;
        }

        /// <summary>
        /// The dormitory's hearth (exposure4 lane; <see cref="SettlementCompound.HearthCell"/>), on the same thermostat
        /// as a house's. Built when the season's mean falls below the comfortable minimum of whoever in the settlement
        /// feels the cold first, since anybody may come to lodge there; lit while somebody in the dormitory is colder
        /// than they find comfortable, and kept lit until they are <see cref="HearthWarmthMargin"/> above it; lit too
        /// for anybody with no finished house of their household's whom the cold has already hurt (hypothermia at the
        /// minor stage), so the search for warmth has somewhere to go. A RimWorld player puts a campfire in the
        /// barracks; the settlement is that player. Before, the dormitory had no hearth, and a household with no house,
        /// or with one a quake had opened, had nowhere warm to go.
        /// </summary>
        private static int TendDormitory(Settlement settlement, Map.Map map, ClimateOutlook outlook)
        {
            SettlementCompound? compound = map.settlementCompound;
            if (compound == null) return 0;
            IntVec3 spot = compound.HearthCell;
            if (!GenGrid.InBounds(spot, map)) return 0;
            Thing? device = DeviceAt(map, spot, ShelterThingDefOf.Campfire);
            bool lit = (device as ThingWithComps)?.GetComp<CompFlickable>()?.WantSwitchOn == true;
            bool someoneCold = SomeoneInDormitoryIsCold(settlement, map, compound, lit ? HearthWarmthMargin : 0f);
            bool coldSeason = TryColdestComfort(settlement, out float comfyMin) && outlook.DailyMean < comfyMin;
            if (device == null && (coldSeason || someoneCold) && ShelterThingDefOf.Campfire.IsResearchFinished) ClearHearthCell(map, spot);
            return TendSpot(map, spot, ShelterThingDefOf.Campfire, someoneCold, coldSeason || someoneCold);
        }

        /// <summary>A dormitory raised before it kept a place for its hearth has a bed standing there
        /// (<see cref="SettlementCompound.BedCells"/> no longer lists that slot): the settlement marks it to come down,
        /// as it marks an old house (<see cref="DwellingPlanner"/>), so the hearth can go in. In the 600-day 40/201
        /// arc every one of the 40 beds stood, and with the bed on the spot no hearth could ever be placed.</summary>
        private static void ClearHearthCell(Map.Map map, IntVec3 spot)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(spot);
            for (int i = 0; i < here.Count; i++)
            {
                Thing t = here[i];
                if (t.def != ConstructionThingDefOf.Bed || t is Blueprint || t is Frame) continue;
                if (map.designationManager.DesignationOn(t, Designations.PlayerDesignationDefOf.Deconstruct) != null) continue;
                map.designationManager.AddDesignation(new Designations.Designation(new AI.LocalTargetInfo(t), Designations.PlayerDesignationDefOf.Deconstruct));
            }
        }

        /// <summary>Whether somebody in the dormitory (the compound's interior) is colder than they find comfortable,
        /// plus <paramref name="margin"/>, or somebody with no finished house is out in the cold with hypothermia at the
        /// minor stage.</summary>
        internal static bool SomeoneInDormitoryIsCold(Settlement settlement, Map.Map map, SettlementCompound compound, float margin)
        {
            CellRect inside = compound.Interior;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead || !p.Spawned || p.Map != map) continue;
                if (!inside.Contains(p.Position))
                {
                    if (FreezingAway(p) && !HasFinishedHome(map, p)) return true;
                    continue;
                }
                float comfyMin = TemperatureInjuryUtility.ComfortableTemperatureRange(p).min;
                if (TemperatureInjuryUtility.AmbientTemperature(p) < comfyMin + margin) return true;
            }
            return false;
        }

        private static bool HasFinishedHome(Map.Map map, Pawn p)
        {
            int family = p.relations.familyId;
            if (family == Pawn_RelationsTracker.None) return false;
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            for (int i = 0; i < dwellings.Count; i++)
            {
                if (dwellings[i].householdId == family && dwellings[i].IsComplete(map)) return true;
            }
            return false;
        }

        /// <summary>The warmest comfortable minimum among the living citizens: the first of them to feel the cold.</summary>
        private static bool TryColdestComfort(Settlement settlement, out float comfyMin)
        {
            comfyMin = float.MinValue;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                if (citizens[i].Dead) continue;
                comfyMin = Math.Max(comfyMin, TemperatureInjuryUtility.ComfortableTemperatureRange(citizens[i]).min);
            }
            return comfyMin > float.MinValue;
        }

        /// <summary>Whether the settlement has asked for <paramref name="dwelling"/>'s hearth to burn.</summary>
        private static bool HearthWanted(Map.Map map, Dwelling dwelling)
        {
            foreach (IntVec3 spot in dwelling.Spots(DwellingPartKind.HearthSpot))
            {
                Thing? device = DeviceAt(map, spot, ShelterThingDefOf.Campfire);
                if ((device as ThingWithComps)?.GetComp<CompFlickable>()?.WantSwitchOn == true) return true;
            }
            return false;
        }

        /// <summary>The household's comfortable range at its narrowest: the warmest of its members' comfortable
        /// minimums and the coolest of their maximums, so nobody in it is left out.</summary>
        internal static bool TryComfortOf(Settlement settlement, int householdId, out FloatRange comfy)
        {
            float min = float.MinValue, max = float.MaxValue;
            bool any = false;
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead || p.relations.familyId != householdId) continue;
                FloatRange r = TemperatureInjuryUtility.ComfortableTemperatureRange(p);
                min = Math.Max(min, r.min);
                max = Math.Min(max, r.max);
                any = true;
            }
            comfy = new FloatRange(min, max);
            return any;
        }

        private static int Tend(Map.Map map, Dwelling dwelling, DwellingPartKind spotKind, ThingDef deviceDef, bool wanted, bool build)
        {
            int placed = 0;
            foreach (IntVec3 spot in dwelling.Spots(spotKind)) placed += TendSpot(map, spot, deviceDef, wanted, build);
            return placed;
        }

        private static int TendSpot(Map.Map map, IntVec3 spot, ThingDef deviceDef, bool wanted, bool build)
        {
            Thing? device = DeviceAt(map, spot, deviceDef);
            if (device == null)
            {
                if (!build || !deviceDef.IsResearchFinished || DwellingPlanner.SiteAt(map, spot)) return 0;
                if (!GenConstruct.CanPlaceBlueprintAt(deviceDef, spot, map, out _)) return 0;
                ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(deviceDef);
                if (blueprintDef == null) return 0;
                GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), spot, map, Rot4.North);
                return 1;
            }
            CompFlickable? flick = (device as ThingWithComps)?.GetComp<CompFlickable>();
            if (flick != null && flick.WantSwitchOn != wanted) flick.SetWantSwitchOn(wanted);
            return 0;
        }

        private static Thing? DeviceAt(Map.Map map, IntVec3 cell, ThingDef def)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if (ReferenceEquals(here[i].def, def)) return here[i];
            }
            return null;
        }
    }
}
