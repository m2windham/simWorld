using System;
using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Research;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// Everything a god looking at their civilization can see, as one plain snapshot
    /// (<c>docs/spec/simworld-spec.md</c> §10 and §12). RimWorld has no equivalent: a RimWorld player looks
    /// straight at the map, and its UI reads live game objects because the game and the UI are one assembly.
    /// SimWorld's core is engine-free on purpose, so the host needs a seam — and this is it.
    ///
    /// <para/><b>Why a snapshot rather than live references.</b> The obvious cheap thing is to hand the host a
    /// <see cref="GodManager"/> and let it read what it likes. That fails three ways at once, and each one has
    /// already bitten some UI somewhere:
    /// <list type="bullet">
    /// <item>A live reference is a write surface. Anything holding a <see cref="EdictDef"/> can reach
    /// <see cref="EdictDef.Worker"/> and drive the simulation from the render thread, and nothing in the type
    /// system says it may not.</item>
    /// <item>A live reference tears. The host renders across frames while the sim ticks; a list read halfway
    /// through a tick shows a civilization that never existed at any single instant.</item>
    /// <item>A live reference is not a contract. Every internal rename becomes a host-side break, so the core
    /// stops being free to refactor — which is most of what being engine-free was for.</item>
    /// </list>
    /// A snapshot is a value taken at one tick. The host may hold it, diff it against the last one, render it
    /// twice, or throw it away, and none of that can touch the simulation.
    ///
    /// <para/><b>Everything is identified by defName, never by Def.</b> A <c>string</c> is stable across a
    /// reload, serializes to anything, and — the point — cannot be used to reach a worker. The host names what
    /// it wants; <see cref="GodCommands"/> resolves the name and decides. That resolution living on this side
    /// of the seam is what makes the read model read-only in fact rather than by convention.
    ///
    /// <para/><b>What this deliberately does not carry.</b> No per-citizen detail: opening every person to
    /// paint a civilization is exactly what §11.3's tiering exists to prevent, and <see cref="GodRollup"/>
    /// already answers "how is my civilization doing" without walking a Statistical cohort. When the host needs
    /// one named citizen it will need its own request for that citizen, and that is a different seam from this
    /// one — a detail query, not a civilization view. No map or rendering data either: what a settlement's
    /// interior looks like belongs to the map layer, and this snapshot only reports whether one exists yet.
    /// </summary>
    public sealed class GodViewSnapshot
    {
        /// <summary>How many chronicle lines <see cref="Capture()"/> carries when the caller does not say.
        /// Enough to read as a recent history, few enough that a snapshot stays cheap to take every time the
        /// view opens. Not sourced from anything — a display default, and the overload exists precisely so a
        /// host that wants a different number does not have to argue with this one.</summary>
        public const int DefaultRecentHistoryCount = 20;

        private GodViewSnapshot(
            int ticksGame,
            string dateLabel,
            CivilizationSummary civilization,
            IReadOnlyList<SettlementSummary> settlements,
            IReadOnlyList<EdictOption> edicts,
            IReadOnlyList<ChronicleLine> recentHistory,
            IReadOnlyList<ChronicleLine> moments)
        {
            TicksGame = ticksGame;
            DateLabel = dateLabel;
            Civilization = civilization;
            Settlements = settlements;
            Edicts = edicts;
            RecentHistory = recentHistory;
            Moments = moments;
        }

        /// <summary>The tick this snapshot was taken at. Every number below is that tick's, not a mixture.</summary>
        public int TicksGame { get; }

        /// <summary>The in-game date, already formatted (<see cref="GenDate.DateReadoutStringAt"/>), so the host
        /// does not reimplement a 60-day-quadrum calendar to print one string.</summary>
        public string DateLabel { get; }

        public CivilizationSummary Civilization { get; }

        /// <summary>Every settlement of the world, in world-object order.</summary>
        public IReadOnlyList<SettlementSummary> Settlements { get; }

        /// <summary>Every edict in content — including ones that cannot be issued right now, each carrying why
        /// not. A view that only received the issuable ones could draw a menu but never explain it.</summary>
        public IReadOnlyList<EdictOption> Edicts { get; }

        /// <summary>The tail of the chronicle, oldest first, so it reads as history rather than a stack.</summary>
        public IReadOnlyList<ChronicleLine> RecentHistory { get; }

        /// <summary>The curated historical moments — the first raid, the first caravan — which the storyteller
        /// keeps separately from the running chronicle because they are the civilization's landmarks rather
        /// than its news.</summary>
        public IReadOnlyList<ChronicleLine> Moments { get; }

        /// <summary>Takes a snapshot of the current game with <see cref="DefaultRecentHistoryCount"/> lines of
        /// history.</summary>
        public static GodViewSnapshot Capture() => Capture(DefaultRecentHistoryCount);

        /// <summary>
        /// Takes a snapshot of the current game.
        ///
        /// <para/>Recomputes <see cref="GodManager.Rollup"/> rather than reading whatever it last held: that
        /// class's own doc says to recompute directly "the instant the god view opens", which is exactly here.
        /// The cost is O(settlements + Full/Interval citizens) and explicitly never O(Statistical population).
        ///
        /// <para/>Safe before a world exists — a game at the main menu has no settlements and no era, and this
        /// reports that rather than throwing, because "nothing founded yet" is a state the view has to draw.
        /// </summary>
        /// <param name="recentHistoryCount">How many chronicle lines to carry. Clamped at zero; a count larger
        /// than the chronicle holds simply yields all of it.</param>
        public static GodViewSnapshot Capture(int recentHistoryCount)
        {
            if (recentHistoryCount < 0) recentHistoryCount = 0;

            int ticks = Find.TickManager.TicksGame;
            World.World? world = Find.World;
            GodManager god = Find.God;

            var settlements = new List<World.Settlement>();
            if (world != null)
            {
                foreach (World.WorldObject obj in world.worldObjects)
                {
                    if (obj is World.Settlement settlement) settlements.Add(settlement);
                }
            }

            god.Rollup.Recompute(settlements);

            var settlementSummaries = new List<SettlementSummary>(settlements.Count);
            for (int i = 0; i < settlements.Count; i++)
            {
                World.Settlement s = settlements[i];
                settlementSummaries.Add(new SettlementSummary(
                    s.name,
                    s.tile,
                    s.foundingTick,
                    s.TotalPopulation,
                    s.Citizens.Count,
                    s.StatisticalPopulation,
                    s.InteriorMap != null));
            }

            return new GodViewSnapshot(
                ticks,
                GenDate.DateReadoutStringAt(ticks, 0f),
                CivilizationSummary.From(god.Rollup, settlements.Count),
                settlementSummaries,
                BuildEdictOptions(god),
                ChronicleLine.TailOf(Find.Storyteller.Chronicle, recentHistoryCount),
                ChronicleLine.TailOf(Find.Storyteller.Moments, recentHistoryCount));
        }

        private static List<EdictOption> BuildEdictOptions(GodManager god)
        {
            IReadOnlyList<EdictDef> all = DefDatabase<EdictDef>.AllDefsListForReading;
            EraDef? era = Find.ResearchManager.CurrentEra;
            bool slotFree = god.ActiveEdicts.Count < GodTuning.MaxActiveEdicts;

            var options = new List<EdictOption>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                options.Add(EdictOption.For(all[i], god.IsActive(all[i]), era, slotFree));
            }
            return options;
        }
    }
}
