using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>How far gone something rottable is (RimWorld: <c>RimWorld.RotStage</c>). Strictly ordered:
    /// a Thing never goes back up the list.</summary>
    public enum RotStage
    {
        Fresh,
        Rotting,
        Dessicated,
    }

    /// <summary>
    /// Data half of <see cref="CompRottable"/> (RimWorld: <c>RimWorld.CompProperties_Rottable</c>). The two
    /// day counts are cumulative from the moment the Thing came into being, not from the previous stage —
    /// <see cref="daysToDessicated"/> is measured from the same zero as <see cref="daysToRotStart"/>.
    /// </summary>
    public class CompProperties_Rottable : CompProperties
    {
        /// <summary>Days at full rot rate before <see cref="RotStage.Rotting"/> begins.</summary>
        public float daysToRotStart = 2.5f;

        /// <summary>Days at full rot rate before <see cref="RotStage.Dessicated"/> begins.</summary>
        public float daysToDessicated = 10f;

        /// <summary>Rotting destroys the Thing outright rather than degrading it — true for food, false for a
        /// corpse (RimWorld ships both, keyed on this same field).</summary>
        public bool rotDestroys;

        public CompProperties_Rottable()
        {
            compClass = typeof(CompRottable);
        }

        public int TicksToRotStart => (int)(daysToRotStart * GenDate.TicksPerDay);

        public int TicksToDessicated => (int)(daysToDessicated * GenDate.TicksPerDay);

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (daysToRotStart < 0f) yield return "CompProperties_Rottable's daysToRotStart must not be negative.";
            if (daysToDessicated <= daysToRotStart)
            {
                yield return "CompProperties_Rottable's daysToDessicated must be greater than daysToRotStart.";
            }
        }
    }

    /// <summary>
    /// Drives Fresh → Rotting → Dessicated over time (RimWorld: <c>RimWorld.CompRottable</c>).
    /// <see cref="RotProgress"/> accumulates in ticks-at-full-rate, so a cold cell advances it slower than a
    /// warm one and a frozen one not at all — the same "progress, not wall clock" shape RimWorld uses, which
    /// is why a corpse in a freezer keeps and one in the sun does not.
    /// <para/>
    /// <b>Tick bucket — <see cref="TickerType.Rare"/>, matching RimWorld.</b> The brief this was built from
    /// asked whether a thing that exists in large numbers belongs on the Rare (250) or Long (2000) list. Rot
    /// is a monotone accumulation over days, so Long would be numerically indistinguishable; Rare is kept
    /// because it is RimWorld's own choice and because <see cref="TickList"/> already buckets by id (a Rare
    /// ticker costs 1/250th of a Normal one per tick, so N corpses cost N/250 calls per tick, not N).
    /// Nothing here allocates or scans, so the per-call cost is a float add and two comparisons.
    /// <para/>
    /// <b>Not ported:</b> RimWorld's <c>rotDamagePerDay</c>/<c>dessicatedDamagePerDay</c> (rot chews a
    /// corpse's hit points as it goes) and weather deterioration. Nothing in this port reads a corpse's hit
    /// points, and a rot that destroyed a body by attrition would silently delete a pawn the chronicle and
    /// the family tree still name — see <see cref="Corpse"/>'s own remarks. A dessicated corpse therefore
    /// persists until something removes it.
    /// </summary>
    public class CompRottable : ThingComp
    {
        private float rotProgress;

        public CompProperties_Rottable Props => (CompProperties_Rottable)props;

        /// <summary>Ticks-at-full-rot-rate accumulated so far (RimWorld: <c>CompRottable.RotProgress</c>).</summary>
        public float RotProgress
        {
            get => rotProgress;
            set => rotProgress = value < 0f ? 0f : value;
        }

        public RotStage Stage
        {
            get
            {
                if (rotProgress < Props.TicksToRotStart) return RotStage.Fresh;
                return rotProgress < Props.TicksToDessicated ? RotStage.Rotting : RotStage.Dessicated;
            }
        }

        /// <summary>Ticks of real time left at the current ambient rot rate before the next stage begins;
        /// <see cref="int.MaxValue"/> when frozen (it would never arrive) and 0 when already dessicated.</summary>
        public int TicksUntilNextStage(float rotRate)
        {
            if (rotRate <= 0f) return int.MaxValue;
            float target = Stage == RotStage.Fresh ? Props.TicksToRotStart : Props.TicksToDessicated;
            if (Stage == RotStage.Dessicated) return 0;
            return (int)((target - rotProgress) / rotRate) + 1;
        }

        public override void CompTickRare()
        {
            base.CompTickRare();
            Tick(GenTicks.TickRareInterval);
        }

        /// <summary>
        /// Advances rot by <paramref name="interval"/> ticks of real time, scaled by the rot rate where the
        /// parent is standing. An unspawned Thing does not rot at all: it has no cell, therefore no
        /// temperature, and in this port an unspawned Thing is one nothing can see (RimWorld keeps rotting a
        /// thing held in a container by reading its holder's map; no container exists here to read).
        /// </summary>
        public void Tick(int interval)
        {
            Map.Map? map = parent.Map;
            if (map == null) return;

            float rate = RotUtility.RotRateAtTemperature(RotUtility.AmbientTemperatureAt(map, parent.Position));
            if (rate <= 0f) return;

            RotProgress += rate * interval;
            if (Props.rotDestroys && Stage >= RotStage.Rotting)
            {
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        public override string CompInspectStringExtra() => Stage.ToString();

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref rotProgress, "rotProgress");
        }
    }

    /// <summary>
    /// Where rot reads the world from (RimWorld: the parts of <c>Verse.GenTemperature</c> a rottable Thing
    /// uses). Kept here rather than as a general <c>GenTemperature</c> so this module adds no file another
    /// lane would also want to own.
    /// </summary>
    public static class RotUtility
    {
        /// <summary>
        /// Temperature at <paramref name="cell"/>: the enclosing room's, or the map's outdoor temperature
        /// when the cell is not in a room (RimWorld: <c>Thing.AmbientTemperature</c>). A room that touches
        /// the outdoors already reports the outdoor temperature itself, so there is no second check for it
        /// here — see <c>Building.RoomGroup.UsesOutdoorTemperature</c>.
        /// </summary>
        public static float AmbientTemperatureAt(Map.Map map, IntVec3 cell)
        {
            if (map == null) return 0f;
            if (!GenGrid.InBounds(cell, map)) return map.outdoorTemperature;
            return map.roomTracker.RoomAt(cell)?.Temperature ?? map.outdoorTemperature;
        }

        /// <summary>
        /// How fast rot runs at <paramref name="temperature"/>°C, as a multiplier on elapsed time (RimWorld:
        /// <c>GenTemperature.RotRateAtTemperature</c>): nothing rots at or below freezing, the rate ramps
        /// linearly up to <see cref="FullRotRateTemperature"/>°C and is flat above it.
        /// <para/>
        /// <b>Unsourced breakpoints.</b> The two temperatures below carry RimWorld's shape (a freezing point
        /// that stops rot dead, a warm plateau) but the exact values could not be re-sourced for this port.
        /// <c>CorpseTests</c> pins the behaviour — frozen keeps indefinitely, cold rots slower than warm,
        /// warmer than the plateau is no faster — rather than the literals.
        /// </summary>
        public const float FreezingTemperature = 0f;

        /// <summary>Warmest temperature that still makes a difference; see <see cref="FreezingTemperature"/>.</summary>
        public const float FullRotRateTemperature = 10f;

        public static float RotRateAtTemperature(float temperature)
        {
            if (temperature <= FreezingTemperature) return 0f;
            if (temperature < FullRotRateTemperature) return temperature / FullRotRateTemperature;
            return 1f;
        }
    }
}
