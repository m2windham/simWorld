using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.World;
using SimWorld.World.Gen;

namespace SimWorld.Factions
{
    /// <summary>
    /// Creates every civilization at world generation (RimWorld: <c>RimWorld.FactionGenerator</c>): one
    /// <see cref="Faction"/> per <see cref="FactionDef"/> instance (counts scaled the same way
    /// <see cref="WorldGenStep_Factions"/> scales settlement counts), named, with pairwise initial
    /// relations, and <see cref="FactionDef.mustStartOneEnemy"/> enforced. Settlement placement itself
    /// stays in <see cref="WorldGenStep_Factions"/>, which calls this first and then places settlements on
    /// the factions this returns.
    /// </summary>
    public static class FactionGenerator
    {
        /// <summary>Creates and registers every non-hidden faction into <paramref name="world"/> and <paramref name="factionManager"/>, in generation order.</summary>
        public static List<Faction> GenerateFactionsIntoWorld(SimWorld.World.World world, FactionManager factionManager, RandomStream rand)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (factionManager == null) throw new ArgumentNullException(nameof(factionManager));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            float popMultiplier = WorldGenStep_Factions.PopulationMultiplier[world.info.overallPopulation];
            var created = new List<Faction>();

            // Name uniqueness is scoped to just this batch (not read from factionManager, whose roster may
            // carry over from an earlier, unrelated generation on the same Find.FactionManager) so that two
            // generations from the same seed always produce the same names, in the same order.
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FactionDef def in DefDatabase<FactionDef>.AllDefsListForReading)
            {
                if (def.hidden) continue;

                int factionCount = WorldGenStep_Factions.FactionCountFor(def, popMultiplier);
                for (int f = 0; f < factionCount; f++)
                {
                    string name = FactionNameMaker.MakeFactionName(def, rand, usedNames);
                    usedNames.Add(name);
                    var faction = new Faction(def, name, world.NextLoadId("Faction"));
                    world.factions.Add(faction);
                    factionManager.Add(faction);
                    created.Add(faction);
                }
            }

            for (int i = 0; i < created.Count; i++)
            {
                for (int j = i + 1; j < created.Count; j++)
                {
                    created[i].TryMakeInitialRelationsWith(created[j], rand);
                }
            }

            EnsureAtLeastOneEnemy(created, rand);
            return created;
        }

        /// <summary>
        /// If the player's def sets <see cref="FactionDef.mustStartOneEnemy"/> and no faction rolled
        /// Hostile to the player, flips one random non-permanent-enemy NPC faction to Hostile at -100
        /// goodwill (RimWorld guarantees a new game always has at least one enemy when asked to).
        /// </summary>
        private static void EnsureAtLeastOneEnemy(List<Faction> created, RandomStream rand)
        {
            Faction? player = created.FirstOrDefault(f => f.def.isPlayer);
            if (player == null || !player.def.mustStartOneEnemy) return;
            if (created.Any(f => !ReferenceEquals(f, player) && f.HostileTo(player))) return;

            var candidates = created.Where(f => !ReferenceEquals(f, player) && !f.def.permanentEnemy).ToList();
            if (candidates.Count == 0) return;

            Faction chosen = rand.Element(candidates);
            chosen.SetRelationDirect(player, FactionRelationKind.Hostile, -100);
        }
    }
}
