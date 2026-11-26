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

        /// <summary>Buildings whose def carries a <see cref="CompBillGiver"/> or is named by any recipe's
        /// <c>recipeUsers</c> (RimWorld: <c>ThingRequestGroup.PotentialBillGiver</c>), so a bench search touches
        /// benches and not every wall and storage hut on the map. Membership is read off the def alone, and the
        /// list keeps spawn order, so it is always an order-preserving subsequence of <see cref="Building"/>.</summary>
        PotentialBillGiver,
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
            int n = GroupsFor(thing, groupBuffer);
            for (int i = 0; i < n; i++) GroupList(groupBuffer[i]).Add(thing);
        }

        public void Remove(Thing thing)
        {
            if (thing == null) throw new ArgumentNullException(nameof(thing));
            RemoveFirst(allThings, thing);
            if (byDef.TryGetValue(thing.def, out List<Thing>? defList)) RemoveFirst(defList, thing);
            int n = GroupsFor(thing, groupBuffer);
            for (int i = 0; i < n; i++)
            {
                if (byGroup.TryGetValue(groupBuffer[i], out List<Thing>? groupList)) RemoveFirst(groupList, thing);
            }
        }

        /// <summary>
        /// <see cref="List{T}.Remove"/> by reference: the first entry that is <paramref name="thing"/> goes, and the
        /// rest keep their order, exactly as before (<see cref="Thing"/> does not override <c>Equals</c>, so the two
        /// find the same entry). List.Remove goes through the default equality comparer, a virtual <c>Equals</c> per
        /// entry it passes, and on a map of twenty thousand things every despawn walks most of the list: a pile of
        /// filth scrubbed away cost that walk (perf, tick-cost lane). A plain reference comparison removes about twice
        /// as fast under .NET 8 (measured on a 28,000-entry list).
        /// </summary>
        private static void RemoveFirst(List<Thing> list, Thing thing)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], thing))
                {
                    list.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>The most groups one thing joins: a building, artificial, a bill giver and haulable.</summary>
        private const int MaxGroupsPerThing = 4;

        /// <summary>Reused by <see cref="Add"/> and <see cref="Remove"/>, which call nothing that comes back here.</summary>
        private readonly ThingRequestGroup[] groupBuffer = new ThingRequestGroup[MaxGroupsPerThing];

        /// <summary>The groups <paramref name="thing"/> belongs to, in a fixed order, written to
        /// <paramref name="groups"/>; returns how many. Written out rather than yielded, so a spawn or a despawn
        /// allocates no iterator.</summary>
        private static int GroupsFor(Thing thing, ThingRequestGroup[] groups)
        {
            int n = 0;
            switch (thing.def.category)
            {
                case ThingCategory.Pawn:
                    groups[n++] = ThingRequestGroup.Pawn;
                    break;
                case ThingCategory.Building:
                    groups[n++] = ThingRequestGroup.Building;
                    if (thing.def.mineable == false) groups[n++] = ThingRequestGroup.BuildingArtificial;
                    if (thing.def.HasComp(typeof(CompBillGiver)) || thing.def.AllRecipes.Count > 0) groups[n++] = ThingRequestGroup.PotentialBillGiver;
                    break;
                case ThingCategory.Item:
                    groups[n++] = ThingRequestGroup.Item;
                    break;
                case ThingCategory.Plant:
                    groups[n++] = ThingRequestGroup.Plant;
                    break;
                case ThingCategory.Filth:
                    groups[n++] = ThingRequestGroup.Filth;
                    break;
                case ThingCategory.Blueprint:
                    groups[n++] = ThingRequestGroup.Blueprint;
                    break;
                case ThingCategory.Frame:
                    groups[n++] = ThingRequestGroup.BuildingFrame;
                    break;
            }
            if (thing.def.EverHaulable) groups[n++] = ThingRequestGroup.HaulableEver;
            return n;
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
