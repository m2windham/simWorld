using System.Collections.Generic;

using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.God.View
{
    /// <summary>One kind of structure in <see cref="RenewalSummary"/>: how many stand, how many are in the era's own
    /// form, and how many are being replaced.</summary>
    public sealed class RenewalCategoryLine
    {
        internal RenewalCategoryLine(string category, int count, int inCurrentEra, int inCurrentOrPreviousEra, int underway)
        {
            Category = category;
            Count = count;
            InCurrentEra = inCurrentEra;
            InCurrentOrPreviousEra = inCurrentOrPreviousEra;
            Underway = underway;
        }

        /// <summary><c>Homes</c>, <c>Storage</c>, <c>Workshops</c> or <c>Walls</c>.</summary>
        public string Category { get; }

        public int Count { get; }

        /// <summary>Of <see cref="Count"/>, those whose form is the current era's.</summary>
        public int InCurrentEra { get; }

        /// <summary>Of <see cref="Count"/>, those whose form is the current era's or the one before.</summary>
        public int InCurrentOrPreviousEra { get; }

        /// <summary>Being replaced now.</summary>
        public int Underway { get; }
    }

    /// <summary>
    /// How far the town has rebuilt itself in the forms of the era it is in (<c>docs/design/era-progression.md</c>
    /// §7.8), over every settlement the god has on a map. The era panel shows <see cref="Share"/> climbing through an
    /// era; §7.5 aims for about half by the middle of an era and four in five when it is left.
    /// <para/>
    /// Homes are the households' houses (<c>Dwelling</c>), by their rung's era. The rest are buildings of the
    /// settlement's own, by the era their research belongs to (<see cref="EraAvailability.EraOf"/>; a building that
    /// needs no research is of the first era): walls not part of a house, storage huts, and benches that take bills.
    /// Roads are not counted yet: they are terrain, and nothing renews them in this lane.
    /// </summary>
    public sealed class RenewalSummary
    {
        internal RenewalSummary(IReadOnlyList<RenewalCategoryLine> categories, int heritageCount, string? waitingFor)
        {
            Categories = categories;
            HeritageCount = heritageCount;
            WaitingFor = waitingFor;
            int total = 0, recent = 0;
            for (int i = 0; i < categories.Count; i++)
            {
                total += categories[i].Count;
                recent += categories[i].InCurrentOrPreviousEra;
            }
            Share = total == 0 ? 1f : (float)recent / total;
        }

        /// <summary>Homes, storage, workshops and walls, in that order (the order renewal breaks ties in).</summary>
        public IReadOnlyList<RenewalCategoryLine> Categories { get; }

        /// <summary>0-1: structures in the current or previous era's form over all of them (§7.5's measure); 1 with
        /// nothing built.</summary>
        public float Share { get; }

        /// <summary>Houses and buildings kept as heritage.</summary>
        public int HeritageCount { get; }

        /// <summary>Why renewal is waiting ("food is short"), or null while it may go ahead.</summary>
        public string? WaitingFor { get; }

        internal static RenewalSummary Capture()
        {
            EraDef? current = Find.ResearchManager.CurrentEra;
            int now = current?.order ?? 0;
            var counts = new int[4, 4]; // category x (count, current, current-or-previous, underway)
            int heritage = 0;
            string? waiting = null;

            World.World? world = Find.World;
            IReadOnlyList<World.WorldObject> objects = world != null ? world.worldObjects : new List<World.WorldObject>();
            for (int o = 0; o < objects.Count; o++)
            {
                if (!(objects[o] is World.Settlement s) || s.InteriorMap == null) continue;
                Map.Map map = s.InteriorMap;
                waiting ??= RenewalInitiative.WaitReason(s, map);

                var houseCells = new HashSet<IntVec3>();
                IReadOnlyList<Dwelling> homes = DwellingUtility.DwellingsOn(map);
                for (int i = 0; i < homes.Count; i++)
                {
                    Dwelling d = homes[i];
                    foreach (IntVec3 c in d.footprint.Cells) houseCells.Add(c);
                    if (d.beingDemolished || d.completedTick < 0) continue;
                    if (d.isHeritage) heritage++;
                    Tally(counts, 0, d.def.era?.order ?? 0, now, d.replacedById != 0);
                }

                IReadOnlyList<Thing> buildings = map.listerThings.ThingsInGroup(ThingRequestGroup.Building);
                for (int i = 0; i < buildings.Count; i++)
                {
                    if (!(buildings[i] is Building.Building b) || b.Faction == null || houseCells.Contains(b.Position)) continue;
                    int category = CategoryOf(b);
                    if (category < 0) continue;
                    if (b.isHeritage) heritage++;
                    // A bench's or wall's form includes its material (StructureRenewal.FormEraOrder): a granite wall is
                    // a Bronze-age wall and a steel one a Medieval wall. A store's form is its def.
                    Tally(counts, category, StructureRenewal.FormEraOrder(b), now, ReplacementOn(map, b.Position));
                }
            }

            string[] names = { "Homes", "Storage", "Workshops", "Walls" };
            var lines = new List<RenewalCategoryLine>(names.Length);
            for (int c = 0; c < names.Length; c++)
            {
                lines.Add(new RenewalCategoryLine(names[c], counts[c, 0], counts[c, 1], counts[c, 2], counts[c, 3]));
            }
            return new RenewalSummary(lines, heritage, waiting);
        }

        private static void Tally(int[,] counts, int category, int eraOrder, int now, bool underway)
        {
            counts[category, 0]++;
            if (eraOrder >= now) counts[category, 1]++;
            if (eraOrder >= now - 1) counts[category, 2]++;
            if (underway) counts[category, 3]++;
        }

        /// <summary>1 storage, 2 workshops, 3 walls, or -1 for anything renewal does not count.</summary>
        private static int CategoryOf(Building.Building b)
        {
            if (StorageForms.IsStorage(b.def)) return 1;
            if (b.GetComp<CompBillGiver>() != null) return 2;
            if (GenConstruct.ReplaceTag(b.def) == "Wall") return 3;
            return -1;
        }

        private static bool ReplacementOn(Map.Map map, IntVec3 cell)
        {
            IReadOnlyList<Thing> here = map.thingGrid.ThingsListAt(cell);
            for (int i = 0; i < here.Count; i++)
            {
                if ((here[i] is Blueprint || here[i] is Frame) && GenConstruct.ThingToReplace(here[i]) != null) return true;
            }
            return false;
        }
    }
}
