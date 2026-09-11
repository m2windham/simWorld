using System.Collections.Generic;

namespace SimWorld.Defs
{
    /// <summary>
    /// What makes a ThingDef a piece of filth (RimWorld: <c>RimWorld.FilthProperties</c>) — the tunables
    /// <see cref="SimWorld.Filth.Filth"/> and <see cref="SimWorld.Filth.FilthMaker"/> read.
    /// <para/>
    /// <b>Translation — <see cref="sourceTerrains"/> is RimWorld's <c>TerrainDef.generatedFilth</c>, inverted.</b>
    /// RimWorld names the filth from the terrain (<c>TerrainDef.generatedFilth</c>, a field on every soil/sand/
    /// gravel def); this names the terrain from the filth. Same relation, same content, read from the other
    /// end — and reading it from this end means the whole feature adds files instead of editing
    /// <c>Map/TerrainDef.cs</c> and every shipped <c>Terrain_*.xml</c> element while four other lanes are live
    /// (CLAUDE.md, "add a file rather than edit a shared one", and its worked example of exactly this
    /// inversion for <c>WorkGiverDef.fixedBillGiverDefs</c>). <see cref="SimWorld.Filth.FilthMaker.FilthFromTerrain"/>
    /// is the lookup that walks it.
    /// <para/>
    /// <b>Not ported:</b> RimWorld's <c>rainWashes</c> (this port has no weather at all — see the Building
    /// module's own report on <c>Map.outdoorTemperature</c> being a flat settable value), and its
    /// <c>placementMote</c>/visual fields, which are renderer concerns an engine-free core has no use for.
    /// </summary>
    public class FilthProperties
    {
        /// <summary>Most times one pile of this filth can be thickened before further filth in the cell is
        /// dropped instead of stacking (RimWorld: <c>FilthProperties.maxThickness</c>, whose vanilla default
        /// is 3). Thickness is what stops filth accumulating without bound in a single cell.</summary>
        public int maxThickness = 3;

        /// <summary>
        /// Work ticks (at a cleaning speed of 1) to take one point of <see cref="SimWorld.Filth.Filth.thickness"/>
        /// off. RimWorld's own field of this name; the vanilla value is not sourced here, so the tests pin the
        /// trend — thicker filth takes proportionally longer, a faster cleaner finishes sooner — rather than
        /// this literal (CLAUDE.md).
        /// </summary>
        public float cleaningWorkToReduceThickness = 40f;

        /// <summary>
        /// Days after which a pile of this filth rots away on its own, rolled per pile when it spawns
        /// (RimWorld: <c>FilthProperties.disappearsInDays</c>). Null means it never leaves by itself — only
        /// cleaning removes it. This is also what decides whether the pile costs a tick at all: see
        /// <see cref="SimWorld.Filth.Filth.TickerType"/>.
        /// </summary>
        public IntRange? disappearsInDays;

        /// <summary>Whether a pawn walking over this picks some of it up to carry elsewhere (RimWorld:
        /// <c>FilthProperties.canFilthAttach</c>). False for filth that stays where it fell.</summary>
        public bool canFilthAttach = true;

        /// <summary>
        /// Terrain a pawn walking over generates this filth on — the inverted
        /// <c>TerrainDef.generatedFilth</c>; see this class's own remarks. Empty/null means this filth is
        /// never produced by terrain and only ever comes from an explicit
        /// <see cref="SimWorld.Filth.FilthMaker.TryMakeFilth"/> call (blood, for one).
        /// </summary>
        public List<Map.TerrainDef>? sourceTerrains;
    }

    /// <summary>
    /// Filth-layer half of <see cref="ThingDef"/> (system: filth). Its own partial file, matching the split
    /// <c>ThingDef.Things.cs</c>/<c>ThingDef.Building.cs</c>/<c>ThingDef.Plant.cs</c> already use — and so the
    /// field lands without touching a <c>ThingDef</c> file another lane may be mid-edit on.
    /// </summary>
    public partial class ThingDef
    {
        /// <summary>Filth tunables; present on <c>category=Filth</c> Defs only.</summary>
        public FilthProperties? filth;
    }
}
