using SimWorld.Building;
using SimWorld.Defs;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Filth
{
    /// <summary>
    /// What a pawn dirties by walking around (RimWorld: <c>Verse.Pawn_FilthTracker</c>, reached from
    /// <c>Pawn_PathFollower.TryEnterNextPathCell</c> via <c>Notify_EnteredNewCell</c> — the same call site
    /// this port hooks). Three things happen when a pawn steps into a cell, and all three are RimWorld's:
    /// bare ground gets churned up underfoot, dirt comes off boots onto the clean floor just inside a door,
    /// and an animal that got indoors leaves something behind.
    /// <para/>
    /// <b>Translation — stateless, so no per-pawn field.</b> RimWorld's tracker is an object on
    /// <c>Pawn</c> holding a short list of filth defs the pawn has picked up, dripped back out over the next
    /// few cells; that is what makes a colonist trail dirt several tiles into a room rather than only across
    /// the threshold. Reproducing it needs a field on <see cref="Pawn"/>, a line in <c>Pawn.InitializeTrackers</c>
    /// and a Scribe block — three edits to a file four other lanes are live in this batch (CLAUDE.md: add a
    /// file rather than edit a shared one). So the carried list is dropped and the deposit is made at the
    /// moment of the crossing instead: stepping from filth-generating ground onto ground that does not
    /// generate leaves dirt on the cell being stepped into. The observable difference is where the trail ends
    /// — one cell in, rather than a few — and the thing that matters is unchanged: walking in from outside
    /// dirties the inside, continuously, so cleaning is never finished. If a later lane widens
    /// <see cref="Pawn"/> for its own reasons, restoring the carried list here is a contained change.
    /// <para/>
    /// <b>Translation — nothing is generated outside <see cref="CleaningBounds"/>, and this one is load
    /// bearing.</b> RimWorld dirties the whole map, open wilderness included, and gets away with it because
    /// <c>FilthProperties.rainWashes</c> clears outdoor filth back off again. This port has no weather at all
    /// (see the Building module's report on <c>Map.outdoorTemperature</c> being a flat settable number), so
    /// nothing would ever remove it: dirt has no <see cref="Defs.FilthProperties.disappearsInDays"/>, and the
    /// cleaning giver will not go outdoors by design. Ported literally, every cell any pawn ever walked would
    /// accumulate up to <c>maxThickness</c> piles of dirt that no mechanism in the codebase can remove —
    /// an unbounded pile of live Things, growing for as long as the game runs. So terrain filth is made only
    /// where something could eventually clean it. Every observable behaviour that matters survives: rooms get
    /// dirty, thresholds collect tracked-in dirt, cleaning always has work. What is lost is outdoor dirt
    /// nobody was ever going to look at, and with it the leak.
    /// <para/>
    /// <b>Every rate below is SimWorld's own.</b> RimWorld's own pickup/drop chances are not sourced here, so
    /// the tests pin behaviour — walking on a dirt floor eventually dirties it, walking in from outside
    /// eventually dirties the floor inside, a pawn standing still dirties nothing — rather than any of these
    /// literals (CLAUDE.md).
    /// </summary>
    public static class Pawn_FilthTracker
    {
        /// <summary>Chance per cell entered that bare, filth-generating ground gets churned up underfoot.</summary>
        public const float TerrainFilthChance = 0.05f;

        /// <summary>Chance per crossing that a pawn stepping off filth-generating ground onto clean ground
        /// leaves some of it behind — the "tracked in on somebody's boots" case, and the reason an indoor
        /// room next to bare ground never stays clean on its own.</summary>
        public const float TrackedInFilthChance = 0.25f;

        /// <summary>Chance per cell entered that an animal indoors leaves droppings. Deliberately small: an
        /// animal crosses a lot of cells, and this is the one source with no upper bound from terrain.</summary>
        public const float AnimalFilthChance = 0.004f;

        /// <summary>
        /// One pawn has just moved from <paramref name="oldCell"/> into <paramref name="newCell"/>
        /// (RimWorld: <c>Pawn_FilthTracker.Notify_EnteredNewCell</c>). Called from
        /// <c>AI.Pawn_PathFollower</c>, which is the only thing in this codebase that walks a spawned pawn
        /// cell by cell.
        /// </summary>
        public static void Notify_EnteredNewCell(Pawn pawn, IntVec3 oldCell, IntVec3 newCell)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead) return;
            Map.Map map = pawn.Map!;

            // Nothing is ever tracked onto ground nobody cleans — see the class remarks on why the open map
            // is excluded rather than dirtied and then ignored.
            if (!CleaningBounds.IsCleanable(map, newCell)) return;

            ThingDef? generatedHere = FilthMaker.FilthFromTerrain(TerrainAt(map, newCell));
            ThingDef? generatedThere = FilthMaker.FilthFromTerrain(TerrainAt(map, oldCell));

            // 1. Bare ground churned up underfoot. This is the half of RimWorld's mechanism that is ported
            //    unchanged: TerrainDef.generatedFilth, read from the filth end.
            if (generatedHere != null && Rand.Chance(TerrainFilthChance))
            {
                FilthMaker.TryMakeFilth(newCell, map, generatedHere, pawn.Label);
            }
            // 2. Stepping off dirt onto something that is not dirt: some of it comes along. See the class
            //    remarks for what this stands in for.
            else if (generatedHere == null && generatedThere != null
                     && generatedThere.filth != null && generatedThere.filth.canFilthAttach
                     && Rand.Chance(TrackedInFilthChance))
            {
                FilthMaker.TryMakeFilth(newCell, map, generatedThere, pawn.Label);
            }

            // 3. An animal that got in where people live. The bounds check above already kept this out of
            //    the wilderness, which is the whole point of animal filth being a nuisance at all.
            if (pawn.RaceProps.Animal && Rand.Chance(AnimalFilthChance))
            {
                FilthMaker.TryMakeFilth(newCell, map, FilthDefOf.Filth_AnimalFilth, pawn.Label);
            }
        }

        private static TerrainDef? TerrainAt(Map.Map map, IntVec3 c) =>
            GenGrid.InBounds(c, map) ? map.terrainGrid.TerrainAt(c) : null;
    }
}
