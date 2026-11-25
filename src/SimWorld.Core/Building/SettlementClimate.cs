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
            IReadOnlyList<Dwelling> dwellings = DwellingUtility.DwellingsOn(map);
            if (dwellings.Count == 0) return 0;
            ClimateOutlook outlook = Outlook(map);
            int placed = 0;
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
        /// is not counted: the hearth cannot warm them, and an empty house burns no wood.
        /// </summary>
        internal static bool SomeoneHomeIsCold(Settlement settlement, Map.Map map, Dwelling dwelling, float margin)
        {
            IReadOnlyList<Pawn> citizens = settlement.Citizens;
            for (int i = 0; i < citizens.Count; i++)
            {
                Pawn p = citizens[i];
                if (p.Dead || !p.Spawned || p.Map != map || p.relations.familyId != dwelling.householdId) continue;
                if (!dwelling.InteriorContains(p.Position)) continue;
                float comfyMin = TemperatureInjuryUtility.ComfortableTemperatureRange(p).min;
                if (TemperatureInjuryUtility.AmbientTemperature(p) < comfyMin + margin) return true;
            }
            return false;
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
            foreach (IntVec3 spot in dwelling.Spots(spotKind))
            {
                Thing? device = DeviceAt(map, spot, deviceDef);
                if (device == null)
                {
                    if (!build || !deviceDef.IsResearchFinished || DwellingPlanner.SiteAt(map, spot)) continue;
                    if (!GenConstruct.CanPlaceBlueprintAt(deviceDef, spot, map, out _)) continue;
                    ThingDef? blueprintDef = GenConstruct.BlueprintDefFor(deviceDef);
                    if (blueprintDef == null) continue;
                    GenSpawn.Spawn(ThingMaker.MakeThing(blueprintDef), spot, map, Rot4.North);
                    placed++;
                    continue;
                }
                CompFlickable? flick = (device as ThingWithComps)?.GetComp<CompFlickable>();
                if (flick != null && flick.WantSwitchOn != wanted) flick.SetWantSwitchOn(wanted);
            }
            return placed;
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
