using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// Every civilization in the current game and lookups over them (RimWorld: <c>RimWorld.FactionManager</c>).
    /// Exposed as <see cref="Find.FactionManager"/>. World generation (<see cref="FactionGenerator"/>) is the
    /// usual source of new factions; nothing here regenerates them, so a caller starting a fresh game should
    /// assign a fresh instance first (mirroring <see cref="Find.TickManager"/>'s convention).
    /// </summary>
    public sealed class FactionManager : IExposable
    {
        private readonly List<Faction> allFactions = new List<Faction>();

        public IReadOnlyList<Faction> AllFactionsListForReading => allFactions;

        /// <summary>Every non-hidden faction (RimWorld: <c>FactionManager.AllFactionsVisibleInViewOrder</c>, minus the ordering).</summary>
        public IEnumerable<Faction> AllFactionsVisible => allFactions.Where(f => !f.def.hidden);

        public void Add(Faction faction)
        {
            if (faction == null) throw new System.ArgumentNullException(nameof(faction));
            if (!allFactions.Contains(faction)) allFactions.Add(faction);
        }

        public bool Remove(Faction faction) => faction != null && allFactions.Remove(faction);

        /// <summary>The player's own civilization, or null if none has been generated yet.</summary>
        public Faction? OfPlayer => allFactions.FirstOrDefault(f => f.def.isPlayer);

        public Faction? FirstFactionOfDef(FactionDef def) => allFactions.FirstOrDefault(f => f.def == def);

        /// <summary>Call once per game tick; ticks every registered faction's goodwill drift.</summary>
        public void FactionManagerTick()
        {
            // Snapshot: a tick could in principle mutate the list (a faction defeated mid-tick); never
            // iterate the live list while that happens.
            var snapshot = allFactions.Count == 0 ? allFactions : new List<Faction>(allFactions);
            for (int i = 0; i < snapshot.Count; i++)
            {
                snapshot[i].FactionTick();
            }
        }

        /// <summary>
        /// Factions matching the given filters (RimWorld: <c>FactionManager.GetFactions</c>).
        /// <paramref name="minTechLevel"/> of <see cref="TechLevel.Undefined"/> means no floor.
        /// </summary>
        public IEnumerable<Faction> GetFactions(bool allowHidden = false, bool allowDefeated = false, bool allowNonHumanlike = true, TechLevel minTechLevel = TechLevel.Undefined)
        {
            foreach (Faction f in allFactions)
            {
                if (!allowHidden && f.def.hidden) continue;
                if (!allowDefeated && f.defeated) continue;
                if (!allowNonHumanlike && !f.def.humanlikeFaction) continue;
                if (minTechLevel != TechLevel.Undefined && f.def.techLevel < minTechLevel) continue;
                yield return f;
            }
        }

        /// <summary>
        /// A hostile faction to raid with, weighted by <see cref="FactionDef.raidCommonality"/> — a faction
        /// twice as common raids about twice as often (RimWorld weights its own raider choice the same way).
        /// The other <c>Random*Faction</c> pickers stay uniform: commonality is about who comes for you, not
        /// about who happens to be allied or neutral.
        /// </summary>
        public Faction? RandomEnemyFaction(bool allowHidden = false, bool allowDefeated = false, bool allowNonHumanlike = true, TechLevel minTechLevel = TechLevel.Undefined)
        {
            Faction? player = OfPlayer;
            if (player == null) return null;
            List<Faction> candidates = GetFactions(allowHidden, allowDefeated, allowNonHumanlike, minTechLevel)
                .Where(f => !ReferenceEquals(f, player) && f.HostileTo(player))
                .ToList();
            if (candidates.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++) total += Math.Max(0f, candidates[i].def.raidCommonality);
            if (total <= 0f) return Rand.Element(candidates); // every candidate weighted to nothing: fall back to uniform.

            float roll = Rand.Value * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                roll -= Math.Max(0f, candidates[i].def.raidCommonality);
                if (roll <= 0f) return candidates[i];
            }
            return candidates[candidates.Count - 1]; // floating-point slack at the very top of the range.
        }

        public Faction? RandomAlliedFaction(bool allowHidden = false, bool allowDefeated = false, bool allowNonHumanlike = true, TechLevel minTechLevel = TechLevel.Undefined)
        {
            return RandomFactionMatching((f, player) => f.RelationKindWith(player) == FactionRelationKind.Ally, allowHidden, allowDefeated, allowNonHumanlike, minTechLevel);
        }

        public Faction? RandomNonHostileFaction(bool allowHidden = false, bool allowDefeated = false, bool allowNonHumanlike = true, TechLevel minTechLevel = TechLevel.Undefined)
        {
            return RandomFactionMatching((f, player) => !f.HostileTo(player), allowHidden, allowDefeated, allowNonHumanlike, minTechLevel);
        }

        /// <summary>Shared implementation: picks uniformly among candidates whose relation with the player satisfies <paramref name="matches"/>.</summary>
        private Faction? RandomFactionMatching(System.Func<Faction, Faction, bool> matches, bool allowHidden, bool allowDefeated, bool allowNonHumanlike, TechLevel minTechLevel)
        {
            Faction? player = OfPlayer;
            if (player == null) return null;
            var candidates = GetFactions(allowHidden, allowDefeated, allowNonHumanlike, minTechLevel)
                .Where(f => !ReferenceEquals(f, player) && matches(f, player))
                .ToList();
            return candidates.Count == 0 ? null : Rand.Element(candidates);
        }

        public void ExposeData()
        {
            List<Faction>? f = new List<Faction>(allFactions);
            Scribe_Collections.Look(ref f, "allFactions", LookMode.Deep);
            allFactions.Clear();
            if (f != null) allFactions.AddRange(f);
        }
    }
}
