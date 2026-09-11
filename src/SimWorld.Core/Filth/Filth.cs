using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Things;

namespace SimWorld.Filth
{
    /// <summary>
    /// A patch of dirt, blood or refuse sitting on one map cell (RimWorld: <c>Verse.Filth</c>). It is an
    /// ordinary <see cref="Thing"/> of <see cref="ThingCategory.Filth"/>, so it coexists with whatever else
    /// occupies its cell — <see cref="Map.ThingGrid"/> keeps a list per cell, not a single occupant, and
    /// <see cref="Map.ListerThings"/> already indexed <see cref="Map.ThingRequestGroup.Filth"/> before this
    /// module existed. Nothing about the map had to be widened to hold it.
    /// <para/>
    /// Filth is never constructed directly by the things that produce it: <see cref="FilthMaker"/> is the one
    /// entry point, exactly as in RimWorld, so that thickening an existing pile rather than stacking a second
    /// one in the same cell is impossible to get wrong at a call site.
    /// <para/>
    /// <b>Tick cost.</b> Filth exists in the hundreds, so which bucket it sits in is a real decision rather
    /// than a formality. <see cref="TickerType"/> below answers <see cref="Sim.TickerType.Never"/> for any
    /// filth that cannot expire — the common case, dirt tracked across a floor — so those piles are never
    /// registered with the tick manager at all and cost exactly nothing per tick. Only filth with a
    /// <see cref="FilthProperties.disappearsInDays"/> joins the Rare list (one bucket in
    /// <see cref="GenTicks.TickRareInterval"/>, so a pile is looked at once every 250 ticks and the per-tick
    /// cost of a thousand of them is four). Deciding per instance is possible because
    /// <see cref="Thing.TickerType"/> is virtual — <see cref="Pawns.Pawn"/> already overrides it per instance
    /// for its simulation tier — and it beats putting <c>tickerType</c> on the def, which would make every
    /// pile of a def pay for the one property that varies by def anyway.
    /// <para/>
    /// <b>Namespace note</b> (CLAUDE.md): <c>SimWorld.Filth</c> is both this namespace and this class, so from
    /// anywhere else a bare <c>Filth</c> binds to the namespace and fails with CS0118. Write
    /// <c>Filth.Filth</c>, as <c>Map.Map</c>, <c>World.World</c> and <c>Building.Building</c> already do.
    /// </summary>
    public class Filth : Thing
    {
        /// <summary>How many sources a pile remembers (RimWorld caps this list too; the cap itself is
        /// unsourced and nothing reads the list but a future inspect pane, so it is a memory bound, not a
        /// tuned number).</summary>
        public const int MaxSources = 3;

        /// <summary>How many layers deep this pile is, 1 up to <see cref="FilthProperties.maxThickness"/>
        /// (RimWorld: <c>Filth.thickness</c>). This is what makes repeated filth in one cell accumulate to a
        /// bound instead of spawning an unbounded number of Things there.</summary>
        public int thickness = 1;

        /// <summary>Who or what tracked this in, most recent last (RimWorld: <c>Filth.sources</c>). Null until
        /// something names a source.</summary>
        private List<string>? sources;

        /// <summary><see cref="Sim.TickManager.TicksGame"/> when this pile was last thickened; drives
        /// <see cref="TicksSinceThickened"/>.</summary>
        private int thickenedTick;

        /// <summary>Absolute tick this pile rots away on, or -1 when it never does (<see cref="FilthProperties.disappearsInDays"/>
        /// unset). Rolled once when the pile first spawns, so two piles made in the same tick do not vanish
        /// together.</summary>
        private int disappearTick = -1;

        public FilthProperties Props => def.filth ?? DefaultProps;

        private static readonly FilthProperties DefaultProps = new FilthProperties();

        /// <summary>True while this pile has room for another layer (RimWorld: <c>Filth.CanBeThickened</c>).</summary>
        public bool CanBeThickened => thickness < Props.maxThickness;

        /// <summary>Ticks since the last layer landed (RimWorld: <c>Filth.TicksSinceThickened</c>). The
        /// cleaning giver uses it to leave filth that is still actively being made alone for a moment — see
        /// <see cref="WorkGiver_CleanFilth"/>.</summary>
        public int TicksSinceThickened => Find.TickManager.TicksGame - thickenedTick;

        /// <summary>Whether this pile expires on its own; see the class remarks on tick cost.</summary>
        public bool DisappearsOnItsOwn => Props.disappearsInDays.HasValue;

        public override string Label =>
            thickness <= 1 ? base.Label : base.Label + " x" + thickness.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Only filth that can expire ever joins a tick list; the rest costs nothing. See the class remarks.</summary>
        public override TickerType TickerType => DisappearsOnItsOwn ? TickerType.Rare : TickerType.Never;

        public override void SpawnSetup(Map.Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                thickenedTick = Find.TickManager.TicksGame;
                if (Props.disappearsInDays.HasValue)
                {
                    disappearTick = Find.TickManager.TicksGame
                        + Rand.Range(Props.disappearsInDays.Value) * GenDate.TicksPerDay;
                }
            }
        }

        /// <summary>Records who tracked this in, keeping the most recent <see cref="MaxSources"/>
        /// (RimWorld: <c>Filth.AddSource</c>).</summary>
        public void AddSource(string? newSource)
        {
            if (string.IsNullOrEmpty(newSource)) return;
            sources ??= new List<string>();
            sources.Add(newSource!);
            while (sources.Count > MaxSources) sources.RemoveAt(0);
        }

        /// <summary>Every source this pile remembers, oldest first.</summary>
        public IReadOnlyList<string> Sources => (IReadOnlyList<string>?)sources ?? System.Array.Empty<string>();

        /// <summary>
        /// Adds a layer, up to <see cref="FilthProperties.maxThickness"/> (RimWorld: <c>Filth.ThickenFilth</c>).
        /// Resets <see cref="TicksSinceThickened"/> whether or not the cap let the layer land, matching
        /// RimWorld: something is still actively dirtying this cell either way.
        /// </summary>
        public void ThickenFilth()
        {
            if (thickness < Props.maxThickness) thickness++;
            thickenedTick = Find.TickManager.TicksGame;
        }

        /// <summary>Takes one layer off, destroying the pile when the last one goes (RimWorld:
        /// <c>Filth.ThinFilth</c>). This is the only way cleaning removes filth.</summary>
        public void ThinFilth()
        {
            thickness--;
            if (thickness <= 0) Destroy();
        }

        public override void TickRare()
        {
            base.TickRare();
            if (disappearTick >= 0 && Find.TickManager.TicksGame >= disappearTick) Destroy();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref thickness, "thickness", 1);
            Scribe_Values.Look(ref thickenedTick, "thickenedTick", 0);
            Scribe_Values.Look(ref disappearTick, "disappearTick", -1);
            Scribe_Collections.Look(ref sources, "sources", LookMode.Value);
        }
    }
}
