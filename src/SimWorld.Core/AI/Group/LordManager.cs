using System.Collections.Generic;

using SimWorld.Factions;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI.Group
{
    /// <summary>
    /// Every lord on one map (RimWorld: <c>Verse.AI.Group.LordManager</c>, <c>Map.lordManager</c>). Ticked from
    /// <see cref="Map.Map.MapTick"/> and saved with the map.
    /// </summary>
    public sealed class LordManager : IExposable
    {
        public readonly Map.Map map;

        public List<Lord> lords = new List<Lord>();

        /// <summary>Next <see cref="Lord.loadID"/> on this map. RimWorld takes lord ids from a game-wide
        /// <c>UniqueIDsManager</c>; this port has none, and a lord is only ever looked up on its own map.</summary>
        private int nextLordID;

        public LordManager(Map.Map map)
        {
            this.map = map;
        }

        public int GetNextLordID() => nextLordID++;

        public void AddLord(Lord newLord)
        {
            lords.Add(newLord);
            newLord.lordManager = this;
        }

        public void RemoveLord(Lord oldLord)
        {
            lords.Remove(oldLord);
            oldLord.Cleanup();
        }

        /// <summary>The lord that owns <paramref name="pawn"/> on this map, or null (RimWorld:
        /// <c>LordManager.LordOf</c>, reached through <c>pawn.GetLord()</c>).</summary>
        public Lord? LordOf(Pawn pawn)
        {
            for (int i = 0; i < lords.Count; i++)
            {
                if (lords[i].ownedPawns.Contains(pawn)) return lords[i];
            }
            return null;
        }

        /// <summary>Ticks every lord in order. A lord can dissolve itself mid-tick (its last member lost),
        /// so the index only advances past a lord that is still there.</summary>
        public void LordManagerTick()
        {
            int i = 0;
            while (i < lords.Count)
            {
                Lord lord = lords[i];
                lord.LordTick();
                if (i < lords.Count && ReferenceEquals(lords[i], lord)) i++;
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref nextLordID, "nextLordID");
            List<Lord>? list = lords;
            Scribe_Collections.Look(ref list, "lords", LookMode.Deep);
            lords = list ?? new List<Lord>();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                for (int i = 0; i < lords.Count; i++) lords[i].lordManager = this;
            }
        }
    }

    /// <summary>Makes a lord and hands it its first pawns (RimWorld: <c>Verse.AI.Group.LordMaker</c>).</summary>
    public static class LordMaker
    {
        /// <summary>
        /// RimWorld's <c>LordMaker.MakeNewLord</c>: a new lord on <paramref name="map"/>, given
        /// <paramref name="lordJob"/>, started in its graph's first toil, then handed
        /// <paramref name="startingPawns"/> one at a time — each of which takes the first toil's duty.
        ///
        /// <para/>The graph seed stands in for RimWorld's <c>Rand.Seed = loadID * 193</c>. The load id alone
        /// would do in RimWorld, where lord ids are game-wide and every visitor and trader before a raid moves
        /// them on; here they count per map from zero, so the first raid of every game would roll the same
        /// give-up time. Seeding from the world as well (<see cref="NamedRand"/>, which moves nobody else's
        /// dice) keeps it a pure function of where the lord is without making it the same everywhere.
        /// </summary>
        public static Lord MakeNewLord(Faction? faction, LordJob lordJob, Map.Map map, IEnumerable<Pawn>? startingPawns = null)
        {
            LordManager manager = map.lordManager;
            var lord = new Lord { faction = faction };
            lord.loadID = manager.GetNextLordID();
            lord.graphSeed = NamedRand.For("Lord_" + map.uniqueID + "_" + lord.loadID).Int;
            manager.AddLord(lord);
            lord.SetJob(lordJob);
            if (lord.Graph?.StartingToil != null) lord.GotoToil(lord.Graph.StartingToil);
            if (startingPawns != null)
            {
                foreach (Pawn pawn in startingPawns) lord.AddPawn(pawn);
            }
            return lord;
        }
    }
}
