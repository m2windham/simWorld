using System.Collections.Generic;
using SimWorld.AI;
using SimWorld.Defs;
using SimWorld.Designations;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Things;
using SimWorld.Work;

namespace SimWorld.Building
{
    public class CompProperties_Flickable : CompProperties
    {
        public CompProperties_Flickable()
        {
            compClass = typeof(CompFlickable);
        }
    }

    /// <summary>
    /// A switch someone has to walk over and flip (RimWorld: <c>RimWorld.CompFlickable</c>, read from
    /// <c>josh-m/RW-Decompile/RimWorld/CompFlickable.cs</c>). <see cref="WantSwitchOn"/> is what has been asked
    /// for and <see cref="SwitchIsOn"/> what the switch is; a <see cref="FlickDesignationDefOf.Flick"/>
    /// designation stands while they differ, and <see cref="WorkGiver_Flick"/> sends a pawn to make them agree.
    /// RimWorld's player asks through a gizmo; here the asker is <see cref="SettlementClimate"/> (or a test),
    /// through <see cref="SetWantSwitchOn"/>, which is RimWorld's gizmo action followed by
    /// <c>FlickUtility.UpdateFlickDesignation</c>. Not ported: the off graphic, the comp signals (nothing here
    /// listens for them), the sound.
    /// </summary>
    public class CompFlickable : ThingComp
    {
        private bool switchOnInt = true;

        private bool wantSwitchOn = true;

        public bool SwitchIsOn
        {
            get => switchOnInt;
            set
            {
                if (switchOnInt == value) return;
                switchOnInt = value;
                // The map view draws the switch (ThingView.SwitchedOn); a flip is a field write the chunk cannot see.
                parent?.Map?.mapView.Notify_ChunkChangedAt(parent.Position);
            }
        }

        public bool WantSwitchOn => wantSwitchOn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref switchOnInt, "switchOn", true);
            Scribe_Values.Look(ref wantSwitchOn, "wantSwitchOn", true);
        }

        public bool WantsFlick() => wantSwitchOn != switchOnInt;

        public void DoFlick() => SwitchIsOn = !SwitchIsOn;

        /// <summary>Asks for the switch to be <paramref name="on"/>, and marks or unmarks it for flicking
        /// (RimWorld: the gizmo's toggle, then <c>FlickUtility.UpdateFlickDesignation</c>).</summary>
        public void SetWantSwitchOn(bool on)
        {
            wantSwitchOn = on;
            FlickUtility.UpdateFlickDesignation(parent);
        }
    }

    /// <summary>RimWorld: <c>RimWorld.FlickUtility</c>. <c>CompSchedule</c> is not ported, so only the switch
    /// decides <see cref="WantsToBeOn"/>.</summary>
    public static class FlickUtility
    {
        public static void UpdateFlickDesignation(Thing t)
        {
            Map.Map? map = t.Map;
            if (map == null) return;
            bool wants = (t as ThingWithComps)?.GetComp<CompFlickable>()?.WantsFlick() ?? false;
            Designation? designation = map.designationManager.DesignationOn(t, FlickDesignationDefOf.Flick);
            if (wants && designation == null) map.designationManager.AddDesignation(new Designation(new LocalTargetInfo(t), FlickDesignationDefOf.Flick));
            else if (!wants && designation != null) map.designationManager.RemoveDesignation(designation);
        }

        public static bool WantsToBeOn(Thing t)
        {
            CompFlickable? flick = (t as ThingWithComps)?.GetComp<CompFlickable>();
            return flick == null || flick.SwitchIsOn;
        }
    }

    /// <summary>
    /// Sends a pawn to flip a switch that is marked for it (RimWorld: <c>RimWorld.WorkGiver_Flick</c>; WorkGiverDef
    /// <c>Flick</c>, BasicWorker, priority 500, from RimWorld 1.0's <c>Core/Defs/WorkGiverDefs/WorkGivers.xml</c>).
    /// RimWorld's <c>MaxPathDanger</c> of Deadly is not carried: this port's scanner has no danger setting.
    /// </summary>
    public sealed class WorkGiver_Flick : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override bool ShouldSkip(Pawn pawn, bool forced = false) =>
            pawn.Map == null || !pawn.Map.designationManager.AnySpawnedDesignationOfDef(FlickDesignationDefOf.Flick);

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map.Map? map = pawn.Map;
            if (map == null) yield break;
            IReadOnlyList<Designation> list = map.designationManager.SpawnedDesignationsOfDef(FlickDesignationDefOf.Flick);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].target.Thing is Thing t && t.Spawned && t.Map == map) yield return t;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn.Map == null || pawn.Map.designationManager.DesignationOn(t, FlickDesignationDefOf.Flick) == null) return false;
            if (!pawn.Map.reservationManager.CanReserve(pawn, t)) return false;
            return Reachability.CanReach(pawn, t, PathEndMode);
        }

        public override Job? JobOnThing(Pawn pawn, Thing t, bool forced = false) => new Job(FlickJobDefOf.Flick, t);
    }

    /// <summary>RimWorld: <c>RimWorld.JobDriver_Flick</c>: walk to it, fifteen ticks at it, flip every switch that
    /// wants flipping, take the mark off. The <c>SwitchesFlicked</c> record is not ported.</summary>
    public sealed class JobDriver_Flick : JobDriver
    {
        public const int FlickDuration = 15;

        public override bool TryMakePreToilReservations() =>
            pawn.Map != null && pawn.Map.reservationManager.CanReserve(pawn, job.GetTarget(TargetIndex.A));

        private bool Gone()
        {
            Thing? t = job.GetTarget(TargetIndex.A).Thing;
            return t == null || t.Destroyed || !t.Spawned;
        }

        private bool Unmarked() =>
            Gone() || pawn.Map!.designationManager.DesignationOn(job.GetTarget(TargetIndex.A).Thing!, FlickDesignationDefOf.Flick) == null;

        public override IEnumerable<Toil> MakeNewToils()
        {
            yield return Toils_Reserve.Reserve(TargetIndex.A).FailOn(Gone);
            Toil go = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            go.FailOn(Unmarked);
            yield return go;
            Toil wait = Toils_General.Wait(FlickDuration);
            wait.FailOn(Unmarked);
            yield return wait;
            yield return Toils_General.Do(() =>
            {
                var thing = (ThingWithComps)job.GetTarget(TargetIndex.A).Thing!;
                CompFlickable? flick = thing.GetComp<CompFlickable>();
                if (flick != null && flick.WantsFlick()) flick.DoFlick();
                Designation? designation = pawn.Map!.designationManager.DesignationOn(thing, FlickDesignationDefOf.Flick);
                if (designation != null) pawn.Map.designationManager.RemoveDesignation(designation);
            });
        }
    }

    /// <summary>Binds the flick designation; its own class so no shared DefOf is edited.</summary>
    [DefOf]
    public static class FlickDesignationDefOf
    {
        public static DesignationDef Flick = null!;
    }

    /// <summary>Binds the flick job; its own class so no shared DefOf is edited.</summary>
    [DefOf]
    public static class FlickJobDefOf
    {
        public static JobDef Flick = null!;
    }
}
