using SimWorld.Map;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Building
{
    /// <summary>
    /// A building with an inside (RimWorld: <c>RimWorld.Building_Casket</c>, the base of cryptosleep caskets and
    /// graves). What it holds is <b>off the map</b> — not spawned, not ticked, not found by any scan of the map's
    /// Things — and lives in <see cref="innerContainer"/> until something opens it or the building goes.
    /// <para/>
    /// <b>What is not ported</b> is everything about a living occupant: RimWorld's casket is also where a
    /// cryptosleep pod keeps a pawn, ticks its contents, and lets someone open it with a job (<c>IOpenable</c>).
    /// A grave holds a body that does none of those, so the contents are not ticked (a buried body does not
    /// rot — it is out of the simulation, as one in a freezer would be; RimWorld's would keep rotting at the
    /// grave's temperature), and <see cref="Open"/> is the call a future exhumation would make. The
    /// contents-known flag and the inspect line are RimWorld's.
    /// </summary>
    public class Building_Casket : Building, IThingHolder
    {
        protected ThingOwner innerContainer;

        protected bool contentsKnown;

        public Building_Casket()
        {
            innerContainer = new ThingOwner(this);
        }

        public bool HasAnyContents => innerContainer.Count > 0;

        public Thing? ContainedThing => innerContainer.Count != 0 ? innerContainer[0] : null;

        public bool CanOpen => HasAnyContents;

        public ThingOwner GetDirectlyHeldThings() => innerContainer;

        public virtual void Open()
        {
            if (!HasAnyContents) return;
            EjectContents();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            ThingOwner? container = innerContainer;
            Scribe_Deep.Look(ref container, "innerContainer", this);
            innerContainer = container ?? new ThingOwner(this);
            Scribe_Values.Look(ref contentsKnown, "contentsKnown", false);
        }

        /// <summary>RimWorld marks a casket's contents known on spawn when the building is the player's. Every
        /// building on a settlement's map is that settlement's own here, so it always is.</summary>
        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            contentsKnown = true;
        }

        public virtual bool Accepts(Thing thing) => innerContainer.CanAcceptAnyOf(thing);

        /// <summary>
        /// Takes <paramref name="thing"/> inside (RimWorld: <c>Building_Casket.TryAcceptThing</c>). False, with
        /// nothing moved, when <see cref="Accepts"/> refuses it. The Thing may be on the map or in another
        /// holder; one a pawn is carrying goes in through <see cref="TryAcceptCarried"/>, so that the hands let
        /// go of it in the same step.
        /// </summary>
        public virtual bool TryAcceptThing(Thing thing)
        {
            if (!Accepts(thing)) return false;
            if (!innerContainer.TryAdd(thing)) return false;
            contentsKnown = true;
            return true;
        }

        /// <summary>
        /// Takes whatever <paramref name="hands"/> hold inside (RimWorld: the transfer in
        /// <c>Toils_Haul.DepositHauledThingInContainer</c>): refused, with the hands unchanged, when they hold
        /// nothing or <see cref="Accepts"/> says no. Exactly one place holds the Thing afterwards.
        /// </summary>
        public virtual bool TryAcceptCarried(Pawns.Pawn_CarryTracker hands)
        {
            Thing? carried = hands.CarriedThing;
            if (carried == null || !Accepts(carried)) return false;
            if (!hands.TryTransferCarriedThingTo(innerContainer)) return false;
            contentsKnown = true;
            return true;
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (innerContainer.Count > 0 && (mode == DestroyMode.Deconstruct || mode == DestroyMode.KillFinalize))
            {
                EjectContents();
            }
            innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
            base.Destroy(mode);
        }

        /// <summary>Puts what is inside back on the map, at the building's own cell (RimWorld puts it at the
        /// interaction cell; this port has none).</summary>
        public virtual void EjectContents()
        {
            Map.Map? map = Map;
            if (map != null) innerContainer.TryDropAll(Position, map);
            contentsKnown = true;
        }

        public virtual string GetInspectString()
        {
            string contents = contentsKnown ? innerContainer.ContentsString : "unknown";
            return "Contains: " + contents;
        }
    }
}
