using System.Collections.Generic;
using SimWorld.Map;
using SimWorld.Things;

namespace SimWorld.God.View
{
    /// <summary>
    /// Where one of a letter's subjects is right now — a raider, an animal of a manhunter pack, the wanderer who
    /// joined — so "jump to" goes where they stand, not where they arrived. RimWorld's letters jump to their
    /// <c>LookTargets</c>, which are live things; this is that, read off as values when the snapshot is taken.
    /// <see cref="EventLocationView"/> stays the record of where it happened.
    /// </summary>
    public sealed class LookTargetNow
    {
        internal LookTargetNow(int thingId, int tile, IntVec3 cell)
        {
            ThingId = thingId;
            Tile = tile;
            Cell = cell;
        }

        /// <summary>The thing's id, as in <see cref="LetterView.LookTargetThingIds"/>.</summary>
        public int ThingId { get; }

        /// <summary>The world tile of the settlement whose map it stands on.</summary>
        public int Tile { get; }

        /// <summary>The cell it stands on now.</summary>
        public IntVec3 Cell { get; }

        /// <summary>Every spawned thing among <paramref name="wanted"/>, found on the game's live maps. A subject
        /// that is dead and gone, or on no generated map, is simply absent: there is nowhere to jump to.</summary>
        internal static Dictionary<int, LookTargetNow> Find(HashSet<int> wanted)
        {
            var found = new Dictionary<int, LookTargetNow>();
            if (wanted.Count == 0) return found;
            Sim.Game? game = Sim.Find.CurrentGame;
            if (game == null) return found;
            foreach (Map.Map map in game.Maps)
            {
                IReadOnlyList<Thing> things = map.listerThings.AllThings;
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.Spawned && wanted.Contains(t.thingIDNumber))
                    {
                        found[t.thingIDNumber] = new LookTargetNow(t.thingIDNumber, map.tile, t.Position);
                    }
                }
            }
            return found;
        }
    }
}
