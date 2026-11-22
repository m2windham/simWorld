using System;
using System.Collections.Generic;
using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// Data half of <see cref="CompRefuelable"/> (RimWorld: <c>RimWorld.CompProperties_Refuelable</c>). The
    /// fields a passive cooler or a campfire uses, with RimWorld's defaults. Left out, because nothing shipped
    /// sets them and each needs a system the port lacks: rain consumption, a configurable target level,
    /// atomic fueling, destroy-on-empty, the difficulty fuel multiplier and every UI field (gizmo, overlay,
    /// icon, labels).
    /// </summary>
    public class CompProperties_Refuelable : CompProperties
    {
        /// <summary>Fuel burned per day (60000 ticks).</summary>
        public float fuelConsumptionRate = 1f;

        public float fuelCapacity = 2f;

        /// <summary>Share of <see cref="fuelCapacity"/> a freshly made thing holds.</summary>
        public float initialFuelPercent;

        /// <summary>Below this share of the target the refuel work giver takes an interest.</summary>
        public float autoRefuelPercent = 0.3f;

        public ThingFilter fuelFilter = new ThingFilter();

        /// <summary>Burn only when the owner reports use (<see cref="CompRefuelable.Notify_UsedThisTick"/>).</summary>
        public bool consumeFuelOnlyWhenUsed;

        public float minimumFueledThreshold;

        public CompProperties_Refuelable()
        {
            compClass = typeof(CompRefuelable);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string e in base.ConfigErrors(parentDef)) yield return e;
            if (fuelFilter.AllowedDefCount == 0) yield return $"{parentDef.defName}: CompProperties_Refuelable allows no fuel.";
        }
    }

    /// <summary>
    /// Fuel a building burns and pawns top up (RimWorld: <c>RimWorld.CompRefuelable</c>, ported for what a
    /// passive cooler needs). The level burns down every tick, a refueler brings it back to full, and a
    /// sibling <see cref="CompHeatPusherGated"/> stops when it runs dry. The flick switch and the deconstruct
    /// and flick designations are not ported, so <see cref="ShouldAutoRefuelNowIgnoringFuelPct"/> keeps only
    /// RimWorld's "not on fire" check.
    /// </summary>
    public class CompRefuelable : ThingComp
    {
        /// <summary>Ticks per day, RimWorld's divisor in <c>ConsumptionRatePerTick</c>.</summary>
        public const float TicksPerDay = 60000f;

        private float fuel;

        public CompProperties_Refuelable Props => (CompProperties_Refuelable)props;

        public float Fuel => fuel;

        /// <summary>The level a refuel fills to. RimWorld's is configurable per building; nothing shipped is,
        /// so it is always <see cref="CompProperties_Refuelable.fuelCapacity"/>.</summary>
        public float TargetFuelLevel => Props.fuelCapacity;

        public float FuelPercentOfTarget => fuel / TargetFuelLevel;

        public bool IsFull => TargetFuelLevel - fuel < 1f;

        public bool HasFuel => fuel > 0f && fuel >= Props.minimumFueledThreshold;

        private float ConsumptionRatePerTick => Props.fuelConsumptionRate / TicksPerDay;

        public bool ShouldAutoRefuelNow =>
            FuelPercentOfTarget <= Props.autoRefuelPercent && !IsFull && TargetFuelLevel > 0f && ShouldAutoRefuelNowIgnoringFuelPct;

        /// <summary>RimWorld also refuses while a flickable is switched off, and while it is marked for flicking
        /// or deconstruction; the flick half is ported (shelter lane), the deconstruct half is not.</summary>
        public bool ShouldAutoRefuelNowIgnoringFuelPct =>
            !parent.IsBurning() && FlickUtility.WantsToBeOn(parent)
            && parent.Map?.designationManager.DesignationOn(parent, FlickDesignationDefOf.Flick) == null;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            fuel = Props.fuelCapacity * Props.initialFuelPercent;
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref fuel, "fuel");
        }

        public override void CompTick()
        {
            base.CompTick();
            // RimWorld: no burn while a flickable is switched off (CompRefuelable.CompTick reads flickComp).
            if (!Props.consumeFuelOnlyWhenUsed && FlickUtility.WantsToBeOn(parent)) ConsumeFuel(ConsumptionRatePerTick);
        }

        public void ConsumeFuel(float amount)
        {
            if (fuel <= 0f) return;
            fuel -= amount;
            if (fuel <= 0f) fuel = 0f;
        }

        /// <summary>Takes from <paramref name="fuelThings"/> what it needs to fill up, destroying what it uses
        /// (RimWorld: <c>CompRefuelable.Refuel(List&lt;Thing&gt;)</c>, non-atomic).</summary>
        public void Refuel(List<Thing> fuelThings)
        {
            int needed = GetFuelCountToFullyRefuel();
            while (needed > 0 && fuelThings.Count > 0)
            {
                Thing thing = fuelThings[fuelThings.Count - 1];
                fuelThings.RemoveAt(fuelThings.Count - 1);
                int used = Math.Min(needed, thing.stackCount);
                Refuel((float)used);
                thing.stackCount -= used;
                // RimWorld splits off and destroys what it used. A stack on the map that is used up goes the same
                // way; one in a pawn's hands is the carrier's to dispose of (Pawn_CarryTracker.DestroyCarriedThing).
                if (thing.stackCount <= 0 && thing.Spawned) thing.Destroy(DestroyMode.Vanish);
                needed -= used;
            }
        }

        public void Refuel(float amount)
        {
            fuel += amount;
            if (fuel > Props.fuelCapacity) fuel = Props.fuelCapacity;
        }

        public void Notify_UsedThisTick() => ConsumeFuel(ConsumptionRatePerTick);

        public int GetFuelCountToFullyRefuel()
        {
            float f = TargetFuelLevel - fuel;
            return Math.Max((int)Math.Ceiling(f), 1);
        }

        public override string? CompInspectStringExtra() =>
            $"Fuel: {fuel:0.#} / {Props.fuelCapacity:0.#}";
    }
}
