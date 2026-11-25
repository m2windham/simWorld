using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// Rival civilizations advance too (era-arc finding 5: "defence scales with neither").
    ///
    /// <para/><b>The RimWorld mechanic.</b> A faction's <see cref="FactionDef.techLevel"/> caps the gear its pawns
    /// are generated with (<see cref="Pawns.Generation.PawnWeaponGenerator"/>), and its roster of
    /// <see cref="Pawns.PawnKindDef"/>s, each priced in <see cref="Pawns.PawnKindDef.combatPower"/>, sets what a
    /// raid's points buy. Both are fixed for the life of a RimWorld game, because a RimWorld game lives in one age.
    ///
    /// <para/><b>Shaped by the arc.</b> Here the game crosses eight eras, and a fixed raider is a stone-age band
    /// forever: its threat could only grow by head count, which is the era multiplier this replaces (commit
    /// 868d0a5). So the world keeps its own clock. A rival civilization enters each era when a nominal settlement
    /// would (<see cref="EraDef.intendedYears"/>, the pacing intent), and like the player it fights with the era it
    /// has mastered, the one before the era it is in (<see cref="ArmsEra"/>): its tech level rises with that era
    /// (never below its def's), and its raiding roster narrows to that era's kinds
    /// (<see cref="Pawns.PawnKindDef.era"/>). Points stay RimWorld's formula; the later kinds cost more of them,
    /// so the same points buy fewer, better-armed raiders.
    ///
    /// <para/><b>A clock, bounded by the player</b> (<c>docs/design/era-progression.md</c> §4). The world does
    /// not wait: a town that falls behind its clock meets raiders a step ahead of it, and one that keeps up meets
    /// its equals. But RimWorld scales what it sends by the colony (wealth and colonists,
    /// <c>StorytellerUtility.DefaultThreatPointsNow</c>), not by the calendar, and <c>the-loop.md</c> settled
    /// that pressure here is scaled to the player's own growth. So the rivals are never more than one era past
    /// the civilization (<see cref="RivalEra"/>): a town two ages behind is outclassed by one age, not two, and
    /// can still claw back. A town ahead of the clock keeps its lead; the clock does not chase it.
    ///
    /// <para/>Stateless: everything is derived from the game clock and the content, so there is nothing to save.
    /// The player's own faction is never advanced here: its age is its research.
    /// </summary>
    public static class FactionAdvancement
    {
        /// <summary>The game day the world enters <paramref name="era"/>: the sum of the
        /// <see cref="EraDef.intendedYears"/> of every era before it, in days. 0 for the first era.</summary>
        public static float DayReached(EraDef era)
        {
            if (era == null) throw new ArgumentNullException(nameof(era));
            float years = 0f;
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            for (int i = 0; i < eras.Count; i++)
            {
                if (eras[i].order < era.order) years += eras[i].intendedYears;
            }
            return years * GenDate.DaysPerYear;
        }

        /// <summary>The latest era the world has reached by <paramref name="ticksGame"/>, or null with no eras loaded.</summary>
        public static EraDef? WorldEraAt(int ticksGame)
        {
            EraDef? reached = null;
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            for (int i = 0; i < eras.Count; i++)
            {
                EraDef era = eras[i];
                // In ticks, not float days: a float day loses the last tick before an era late in the ladder.
                if ((double)DayReached(era) * GenDate.TicksPerDay > ticksGame) continue;
                if (reached == null || era.order > reached.order) reached = era;
            }
            return reached;
        }

        /// <summary>The world's era now (tick 0 when no game clock runs, e.g. during world generation).</summary>
        public static EraDef? WorldEra() => WorldEraAt(Find.TickManager?.TicksGame ?? 0);

        /// <summary>
        /// The era the rival civilizations are in: the world's (<see cref="WorldEra"/>), but never more than one
        /// era past the era the player's civilization is in (<see cref="ResearchManager.CurrentEra"/>).
        /// </summary>
        public static EraDef? RivalEra()
        {
            EraDef? world = WorldEra();
            if (world == null) return null;
            ResearchManager research = Find.ResearchManager;
            EraDef? player = research.CurrentEra;
            if (player == null || world.order <= player.order + 1) return world;
            return research.NextEra ?? world;
        }

        /// <summary>The era a civilization in <paramref name="era"/> fights with: the one before it, the era it has
        /// mastered, as <see cref="ResearchManager.MasteredEra"/> is for the player. Null in the first era.</summary>
        public static EraDef? ArmsEra(EraDef? era)
        {
            if (era == null) return null;
            EraDef? before = null;
            IReadOnlyList<EraDef> eras = DefDatabase<EraDef>.AllDefsListForReading;
            for (int i = 0; i < eras.Count; i++)
            {
                if (eras[i].order < era.order && (before == null || eras[i].order > before.order)) before = eras[i];
            }
            return before;
        }

        /// <summary>Whether <paramref name="faction"/> advances with the world: every faction but the player's.</summary>
        public static bool Advances(Faction? faction) => faction != null && !faction.def.isPlayer;

        /// <summary>The era <paramref name="faction"/> is in: <see cref="RivalEra"/>, for a rival; null for the
        /// player (whose age is its research) and for no faction.</summary>
        public static EraDef? EraOf(Faction? faction) => Advances(faction) ? RivalEra() : null;

        /// <summary>
        /// The tech level <paramref name="faction"/>'s gear is capped at: its def's, raised to its arms era's for
        /// a rival (a Neolithic tribe in a Medieval world forges steel). The player keeps its def's.
        /// </summary>
        public static TechLevel TechLevelOf(Faction faction)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            TechLevel own = faction.def.techLevel;
            EraDef? era = ArmsEra(EraOf(faction));
            return era != null && era.techLevel > own ? era.techLevel : own;
        }

        /// <summary>
        /// <paramref name="options"/> narrowed to what <paramref name="faction"/> fields now: the kinds of the
        /// latest era it has mastered (<see cref="ArmsEra"/>) that the roster offers. A kind with no <see cref="Pawns.PawnKindDef.era"/>
        /// counts as before every era, so a roster of such kinds (the outlanders') is returned whole, and a
        /// tribe fields its stone-age band until its first era kinds come due. The player's faction, and any
        /// roster with nothing to narrow, come back unchanged. Costs no random draw.
        /// </summary>
        public static List<PawnGenOption> CurrentRoster(List<PawnGenOption> options, Faction? faction)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (!Advances(faction)) return options;
            int reached = ArmsEra(EraOf(faction))?.order ?? -1;

            int best = int.MinValue;
            for (int i = 0; i < options.Count; i++)
            {
                int tier = Tier(options[i]);
                if (tier <= reached && tier > best) best = tier;
            }
            if (best == int.MinValue) return options;

            var kept = new List<PawnGenOption>(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                if (Tier(options[i]) == best) kept.Add(options[i]);
            }
            return kept.Count == options.Count ? options : kept;
        }

        private static int Tier(PawnGenOption option) => option.kind?.era?.order ?? -1;
    }
}
