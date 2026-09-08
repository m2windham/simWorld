using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Things;

namespace SimWorld.Map
{
    /// <summary>Broad category a caller can ask <see cref="ListerThings.ThingsInGroup"/> for (RimWorld: <c>Verse.ThingRequestGroup</c>).</summary>
    public enum ThingRequestGroup
    {
        Undefined,
        Everything,
        Pawn,
        Building,
        Item,
        Plant,
        HaulableEver,
        BuildingArtificial,
        Filth,

        /// <summary>Blueprints awaiting materials (system 16: Building).</summary>
        Blueprint,

        /// <summary>Frames under construction (system 16: Building).</summary>
        BuildingFrame,
    }

    /// <summary>Every spawned Thing on the map, indexed by def and by broad category (RimWorld: <c>Verse.ListerThings</c>).</summary>
    public sealed class ListerThings
    {
        private readonly List<Thing> allThings = new List<Thing>();
        private readonly Dictionary<ThingDef, List<Thing>> byDef = new Dictionary<ThingDef, List<Thing>>();
        private readonly Dictionary<ThingRequestGroup, List<Thing>> byGroup = new Dictionary<ThingRequestGroup, List<Thing>>();

        public IReadOnlyList<Thing> AllThings => allThings;

        public IReadOnlyList<Thing> ThingsOfDef(ThingDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            return byDef.TryGetValue(def, out List<Thing>? list) ? list : Array.Empty<Thing>();
        }

        public IReadOnlyList<Thing> ThingsInGroup(ThingRequestGroup group)
        {
            if (group == ThingRequestGroup.Everything) return allThings;
            return byGroup.TryGetValue(group, out List<Thing>? list) ? list : Array.Empty<Thing>();
        }

        public void Add(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            allThings.Add(thing);
            DefList(thing.def).Add(thing);
            foreach (ThingRequestGroup group in GroupsFor(thing))
            {
                GroupList(group).Add(thing);
            }
        }

        public void Remove(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            allThings.Remove(thing);
            if (byDef.TryGetValue(thing.def, out List<Thing>? defList)) defList.Remove(thing);
            foreach (ThingRequestGroup group in GroupsFor(thing))
            {
                if (byGroup.TryGetValue(group, out List<Thing>? groupList)) groupList.Remove(thing);
            }
        }

        private static IEnumerable<ThingRequestGroup> GroupsFor(Thing thing)
        {
            switch (thing.def.category)
            {
                case ThingCategory.Pawn:
                    yield return ThingRequestGroup.Pawn;
                    break;
                case ThingCategory.Building:
                    yield return ThingRequestGroup.Building;
                    if (thing.def.mineable == false) yield return ThingRequestGroup.BuildingArtificial;
                    break;
                case ThingCategory.Item:
                    yield return ThingRequestGroup.Item;
                    break;
                case ThingCategory.Plant:
                    yield return ThingRequestGroup.Plant;
                    break;
                case ThingCategory.Filth:
                    yield return ThingRequestGroup.Filth;
                    break;
                case ThingCategory.Blueprint:
                    yield return ThingRequestGroup.Blueprint;
                    break;
                case ThingCategory.Frame:
                    yield return ThingRequestGroup.BuildingFrame;
                    break;
            }
            if (thing.def.EverHaulable) yield return ThingRequestGroup.HaulableEver;
        }

        private List<Thing> DefList(ThingDef def)
        {
            if (!byDef.TryGetValue(def, out List<Thing>? list))
            {
                list = new List<Thing>();
                byDef[def] = list;
            }
            return list;
        }

        private List<Thing> GroupList(ThingRequestGroup group)
        {
            if (!byGroup.TryGetValue(group, out List<Thing>? list))
            {
                list = new List<Thing>();
                byGroup[group] = list;
            }
            return list;
        }
    }
}
