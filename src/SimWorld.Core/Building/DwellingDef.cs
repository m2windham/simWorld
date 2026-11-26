using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Research;

namespace SimWorld.Building
{
    /// <summary>What one cell of a <see cref="DwellingDef.layout"/> is.</summary>
    public enum DwellingPartKind : byte
    {
        /// <summary>Not the house's: ground the plot leaves alone (outside the walls, or an open court).</summary>
        Outside,
        /// <summary>Floor inside the walls.</summary>
        Floor,
        Wall,
        Door,
        /// <summary>Where a bed is placed: it covers this cell and the one behind it (<see cref="BedFoot"/>).</summary>
        Bed,
        BedFoot,
        /// <summary>Floor kept for a hearth (<see cref="ShelterThingDefOf.Campfire"/>), built when the cold calls for one.</summary>
        HearthSpot,
        /// <summary>Floor kept for a cooler (<see cref="ShelterThingDefOf.PassiveCooler"/>), built when the heat calls for one.</summary>
        CoolerSpot,
        /// <summary>Floor kept for the household's table, furnished once the town can make one (<see cref="HouseFurnishing"/>).</summary>
        TableSpot,
        /// <summary>Floor kept for the furnishings of the household's age, placed once the town can make one (<see cref="HouseFurnishing"/>).</summary>
        FurnishingSpot,
    }

    /// <summary>
    /// One rung of the dwelling ladder: a house a household lives in, drawn as a plan of real walls, a door and
    /// beds that builders raise through the ordinary blueprint, frame and <see cref="GenConstruct"/> pipeline
    /// (shelter lane; translation: citizen-initiated construction). RimWorld has no house to port: a home there is
    /// a room the player walls in round beds, and that is exactly what this draws, so everything RimWorld does to
    /// a room happens to it. <see cref="AutoBuildRoofAreaSetter"/> roofs it because its walls carry the
    /// settlement's faction, <see cref="RoomTracker"/> gives it its own air, fire burns its wooden walls, raiders
    /// break them, and a quake brings its roof down.
    ///
    /// <para/><b>The era is in the plan, the material and the roof.</b> A rung's research prerequisites say when it
    /// can be built; its <see cref="wallDef"/> or the era's wall (<see cref="StoneWallMaterials"/>) what it is built
    /// of; its <see cref="roofDef"/> what goes over it. Which of those keeps people alive in a heat wave or a cold
    /// snap is RimWorld's room temperature model's answer, not a number written here: see <c>Dwellings.xml</c>.
    ///
    /// <para/><b>The layout.</b> Rows of characters, the back of the house first and the front (the side that faces
    /// the street) last; a house turned North is drawn as written, north up. <c>#</c> wall, <c>D</c> door,
    /// <c>B</c> a bed's head and <c>b</c> its foot (the cell behind it), <c>h</c> a hearth spot, <c>c</c> a cooler
    /// spot, <c>t</c> a table spot, <c>f</c> a furnishing spot, <c>.</c> floor, and <c>,</c> or a space for ground the house keeps clear but does not enclose.
    /// </summary>
    public class DwellingDef : Def
    {
        /// <summary>Position on the ladder; the newest rung whose research is finished is the one built.</summary>
        public int rung;

        /// <summary>The era this rung belongs to, for reading the ladder against the arc (tests check it agrees
        /// with the era of its research).</summary>
        public EraDef? era;

        public List<string> layout = new List<string>();

        public List<ResearchProjectDef> researchPrerequisites = new List<ResearchProjectDef>();

        /// <summary>The wall every <c>#</c> is built as. Null: the era's wall, the one
        /// <see cref="StoneWallMaterials.PreferredWallDef"/> chooses for the settlement's own ring.</summary>
        public ThingDef? wallDef;

        /// <summary>The stuff a plain <see cref="ConstructionThingDefOf.Wall"/> is built of when the settlement has
        /// it on hand (steel, for the later rungs). Null: wood, the wall's default.</summary>
        public ThingDef? wallStuff;

        /// <summary>The roof <see cref="WorkGiver_BuildRoof"/> lays over this house. Null: RimWorld's constructed roof.</summary>
        public RoofDef? roofDef;

        [Unsaved] private DwellingPartKind[,]? parts;

        /// <summary>Width (along the street) and depth of the plan.</summary>
        public IntVec2 Size => new IntVec2(layout.Count == 0 ? 0 : layout[0].Length, layout.Count);

        /// <summary>What the plan holds at (<paramref name="x"/>, <paramref name="z"/>), with z = 0 the front row.</summary>
        public DwellingPartKind PartAt(int x, int z)
        {
            parts ??= Parse();
            if (x < 0 || z < 0 || x >= parts.GetLength(0) || z >= parts.GetLength(1)) return DwellingPartKind.Outside;
            return parts[x, z];
        }

        public bool IsResearchFinished
        {
            get
            {
                for (int i = 0; i < researchPrerequisites.Count; i++)
                {
                    if (!researchPrerequisites[i].IsFinished) return false;
                }
                return true;
            }
        }

        /// <summary>How many beds the plan holds.</summary>
        public int BedCount
        {
            get
            {
                int n = 0;
                IntVec2 size = Size;
                for (int x = 0; x < size.x; x++)
                {
                    for (int z = 0; z < size.z; z++)
                    {
                        if (PartAt(x, z) == DwellingPartKind.Bed) n++;
                    }
                }
                return n;
            }
        }

        private DwellingPartKind[,] Parse()
        {
            IntVec2 size = Size;
            var result = new DwellingPartKind[size.x, size.z];
            for (int row = 0; row < layout.Count; row++)
            {
                string line = layout[row];
                int z = layout.Count - 1 - row;
                for (int x = 0; x < size.x && x < line.Length; x++) result[x, z] = KindOf(line[x]);
            }
            return result;
        }

        private static DwellingPartKind KindOf(char c) => c switch
        {
            '#' => DwellingPartKind.Wall,
            'D' => DwellingPartKind.Door,
            'B' => DwellingPartKind.Bed,
            'b' => DwellingPartKind.BedFoot,
            'h' => DwellingPartKind.HearthSpot,
            'c' => DwellingPartKind.CoolerSpot,
            't' => DwellingPartKind.TableSpot,
            'f' => DwellingPartKind.FurnishingSpot,
            '.' => DwellingPartKind.Floor,
            _ => DwellingPartKind.Outside,
        };

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors()) yield return e;
            if (layout.Count == 0) yield return defName + ": a dwelling needs a layout";
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Length != layout[0].Length) yield return defName + ": layout row " + i + " is not as wide as the first";
                foreach (char c in layout[i])
                {
                    if ("#DBbhctf., ".IndexOf(c) < 0) yield return defName + ": layout has an unknown character '" + c + "'";
                }
            }
            if (layout.Count == 0) yield break;
            IntVec2 size = Size;
            bool door = false;
            for (int x = 0; x < size.x; x++)
            {
                for (int z = 0; z < size.z; z++)
                {
                    DwellingPartKind k = PartAt(x, z);
                    if (k == DwellingPartKind.Door) door = true;
                    if (k == DwellingPartKind.Bed && PartAt(x, z + 1) != DwellingPartKind.BedFoot) yield return defName + ": the bed at (" + x + "," + z + ") has no foot behind it";
                    if (k == DwellingPartKind.BedFoot && PartAt(x, z - 1) != DwellingPartKind.Bed) yield return defName + ": a bed foot at (" + x + "," + z + ") has no head in front of it";
                }
            }
            if (!door) yield return defName + ": a dwelling needs a door";
            if (BedCount == 0) yield return defName + ": a dwelling needs a bed";
        }
    }
}
