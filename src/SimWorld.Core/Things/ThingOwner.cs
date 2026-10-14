using System;
using System.Collections.Generic;
using System.Text;
using SimWorld.Map;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// Something that holds Things inside itself instead of on a map cell (RimWorld: <c>Verse.IThingHolder</c>,
    /// trimmed to the one member this port reads). The first holder is <c>Building.Building_Casket</c>, so the
    /// first Things to be held are the dead in a grave.
    /// </summary>
    public interface IThingHolder
    {
        ThingOwner GetDirectlyHeldThings();
    }

    /// <summary>
    /// A small list of Things held inside an <see cref="IThingHolder"/> (RimWorld: <c>Verse.ThingOwner</c>, of
    /// which <c>ThingOwner&lt;Thing&gt;</c> with <c>LookMode.Deep</c> is the shape a casket uses).
    /// <para/>
    /// <b>What is ported, and what is not.</b> RimWorld's owner is a large class: stack merging, the
    /// <c>ThingOwnerUtility</c> tree walks, <c>TryTransferToContainer</c> with counts, a placement search for
    /// where dropped contents land. Every holder this port has holds one body, which is never stacked, so this
    /// keeps the part that is the point and nothing else. A held Thing is <b>off the map</b>
    /// (<see cref="Thing.Spawned"/> is false, so nothing sees it, ticks it, or finds it through
    /// <c>ListerThings</c>) and <b>knows where it is</b> (<see cref="Thing.holdingOwner"/>, RimWorld's field of
    /// the same name), which is what lets <c>StoreUtility.StoringThing</c> answer "is this lying about, or put
    /// away".
    /// <para/>
    /// <b>Held Things are deep-saved here and re-linked on load</b>, as RimWorld's owner does with its
    /// contents: the holder is the only place a save can find a body that is not on the map, so it owns the
    /// body's data, and <see cref="Thing.holdingOwner"/> is never written — it is set from this side on load.
    /// </summary>
    public sealed class ThingOwner : IExposable
    {
        private readonly IThingHolder? owner;
        private List<Thing> innerList = new List<Thing>();

        /// <summary>Scribe reconstructs an owner by passing the holder as the one constructor argument.</summary>
        public ThingOwner(IThingHolder owner)
        {
            this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public IThingHolder? Owner => owner;

        public int Count => innerList.Count;

        public bool Any => innerList.Count > 0;

        public Thing this[int index] => innerList[index];

        public IReadOnlyList<Thing> InnerListForReading => innerList;

        public bool Contains(Thing thing) => innerList.Contains(thing);

        /// <summary>RimWorld: <c>ThingOwner.CanAcceptAnyOf</c>. A destroyed Thing, or an empty stack, is nothing
        /// to hold.</summary>
        public bool CanAcceptAnyOf(Thing thing) => thing != null && !thing.Destroyed && thing.stackCount > 0;

        /// <summary>
        /// Takes <paramref name="thing"/> into this owner (RimWorld: <c>ThingOwner.TryAdd</c>). A Thing that is
        /// on a map is taken off it; one held by another owner is taken out of that first, so it is never in
        /// two places. False, with nothing changed, when it cannot be held at all.
        /// </summary>
        public bool TryAdd(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (!CanAcceptAnyOf(thing)) return false;
            if (thing.holdingOwner == this) return true;

            thing.holdingOwner?.Remove(thing);
            if (thing.Spawned) thing.DeSpawn();

            innerList.Add(thing);
            thing.holdingOwner = this;
            return true;
        }

        /// <summary>Lets go of <paramref name="thing"/> without placing it anywhere (RimWorld:
        /// <c>ThingOwner.Remove</c>). The caller owns it from here.</summary>
        public bool Remove(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            if (!innerList.Remove(thing)) return false;
            if (ReferenceEquals(thing.holdingOwner, this)) thing.holdingOwner = null;
            return true;
        }

        /// <summary>
        /// Puts everything held onto <paramref name="dropLoc"/> on <paramref name="map"/> (RimWorld:
        /// <c>ThingOwner.TryDropAll</c> with <c>ThingPlaceMode.Near</c>, trimmed to the cell itself — nothing
        /// this port holds is stacked, and a body lying on its grave's own cell is where an ejected one lands
        /// in every case it can reach). Returns how many were put down.
        /// </summary>
        public int TryDropAll(IntVec3 dropLoc, Map.Map map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (!GenGrid.InBounds(dropLoc, map)) return 0;

            var dropped = new List<Thing>(innerList);
            int count = 0;
            for (int i = 0; i < dropped.Count; i++)
            {
                Thing thing = dropped[i];
                Remove(thing);
                if (thing.Destroyed) continue;
                GenSpawn.Spawn(thing, dropLoc, map);
                count++;
            }
            return count;
        }

        /// <summary>Destroys everything held (RimWorld: <c>ThingOwner.ClearAndDestroyContents</c>).</summary>
        public void ClearAndDestroyContents(DestroyMode mode = DestroyMode.Vanish)
        {
            var doomed = new List<Thing>(innerList);
            for (int i = 0; i < doomed.Count; i++)
            {
                Remove(doomed[i]);
                doomed[i].Destroy(mode);
            }
        }

        /// <summary>"Bob's corpse, Alice's corpse" (RimWorld: <c>ThingOwner.ContentsString</c>).</summary>
        public string ContentsString
        {
            get
            {
                if (innerList.Count == 0) return "nothing";
                var sb = new StringBuilder();
                for (int i = 0; i < innerList.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(innerList[i].Label);
                }
                return sb.ToString();
            }
        }

        public void ExposeData()
        {
            List<Thing>? list = innerList;
            Scribe_Collections.Look(ref list, "innerList", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                innerList = list ?? new List<Thing>();
                innerList.RemoveAll(t => t == null);
                for (int i = 0; i < innerList.Count; i++) innerList[i].holdingOwner = this;
            }
        }
    }
}
