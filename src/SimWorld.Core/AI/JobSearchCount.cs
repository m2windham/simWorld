using System.Collections.Generic;
using System.Linq;
using SimWorld.Health;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.AI
{
    /// <summary>Measurement only, never merged: counts main-tree searches per pawn and outcome, dumped every in-game day.</summary>
    internal static class JobSearchCount
    {
        private static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> Last = new Dictionary<string, string>();
        private static int day = -1;

        public static void Note(Pawn pawn, ThinkResult result)
        {
            int now = Find.TickManager.TicksGame / 60000;
            if (now != day)
            {
                if (day >= 0)
                {
                    int total = Counts.Values.Sum();
                    System.Console.Error.WriteLine("JSC day " + day + " total " + total);
                    foreach (KeyValuePair<string, int> kv in Counts.OrderByDescending(k => k.Value).Take(6))
                    {
                        System.Console.Error.WriteLine("JSC   " + kv.Value + " " + kv.Key + " :: " + Last[kv.Key]);
                    }
                }
                Counts.Clear();
                Last.Clear();
                day = now;
            }
            string key = pawn.Label + pawn.thingIDNumber + "|" + (pawn.Faction == null ? "nofaction" : pawn.Faction.ToString()) + "|"
                + (result.IsValid ? result.Job!.def.defName + "<" + result.SourceNode!.GetType().Name : "NOJOB");
            Counts.TryGetValue(key, out int n);
            Counts[key] = n + 1;
            if ((n & 1023) == 0)
            {
                Last[key] = "at " + pawn.Position + " race " + pawn.def.defName
                    + " moving " + pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving).ToString("0.00")
                    + " mental " + pawn.InMentalState
                    + (result.IsValid ? " target " + result.Job!.targetA.ToString() : string.Empty);
            }
        }
    }
}
