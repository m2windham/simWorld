using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Sim;

namespace SimWorld.God.View
{
    /// <summary>
    /// The write half of the storyteller lever — see <c>GodViewSnapshot.Storyteller.cs</c> for the read half a
    /// host needs before it can offer this at all.
    ///
    /// <para/><b>The gap this closes.</b> <c>docs/design/the-loop.md</c> settles SimWorld as one settlement
    /// whose pressure comes entirely from a storyteller scaled to the player's own growth. That makes the
    /// storyteller and the difficulty the largest single statement a player makes about what kind of run they
    /// want — and the only way to make it was <c>Sim.Game.NewGame(..., storytellerDef, difficultyDef)</c>,
    /// which the host is told never to reach into (CLAUDE.md: "the host binds to God/View and never reaches
    /// into GodManager"), and which can only be answered once, at world generation. RimWorld — the benchmark
    /// — lets a player change both from the Storyteller tab at any point in a run. So does this.
    ///
    /// <para/><b>Why this is a lever and not a setter (<c>docs/design/player-first.md</c> §2).</b> The
    /// distinction is the whole discipline, so it is worth being exact. Neither
    /// <see cref="Director.Storyteller.def"/> nor <see cref="Director.Storyteller.difficulty"/> is a result the
    /// player would be overwriting; both are declared preferences that the simulation reads afresh every time
    /// it decides anything. <see cref="StorytellerUtility.DefaultThreatPointsNow"/> multiplies by
    /// <see cref="DifficultyDef.threatScale"/> and by the storyteller's own
    /// <see cref="StorytellerDef.pointsFactorFromDaysPassed"/> on every single call;
    /// <see cref="StoryWatcher_Adaptation"/> reads the difficulty each interval;
    /// <see cref="DifficultyUtility"/> is consulted live by mood, crop yield, research speed and quest reward;
    /// <see cref="StorytellerComp_Disease"/> reads <see cref="DifficultyDef.diseaseIntervalFactor"/> when it
    /// rolls. Nothing caches either def. The consequence of the change is therefore computed by the machinery
    /// afterwards, which is the test §2 sets — as opposed to "set her age to 40", which writes the answer
    /// itself and has no machinery behind it at all.
    ///
    /// <para/><b>What is refused, and what is emphatically not.</b> Only the impossible: an empty or unknown
    /// defName, and a change asked for when no game is running. A <i>regrettable</i> choice is carried out
    /// exactly as asked — dropping to Peaceful two hundred days in, or switching to Extreme with a starving
    /// settlement, are both allowed, and neither is softened. <c>docs/design/player-first.md</c> §5: "never
    /// refuse the unwise". Nothing is reset on the way through either: the chronicle, the moments, the death
    /// and resource ledgers, the accumulated adaptation and the queued incidents all survive a change of
    /// narrator, because they are the record of what this civilization has been through and the player did not
    /// ask to un-live it.
    ///
    /// <para/><b>Why no game means refusal rather than a quiet success.</b> <see cref="Find.Storyteller"/>
    /// auto-creates a bare <see cref="Director.Storyteller"/> when no game exists, so both methods would
    /// happily write onto it and report <see cref="GodCommandOutcome.Done"/> — and
    /// <c>Sim.Game.NewGame</c> then assigns <c>game.Storyteller = new Storyteller(storytellerDef, difficultyDef)</c>
    /// and that object is discarded unread. That is precisely the failure this project keeps finding: a guard
    /// that passes while the thing behind it is gone. Before a game the choice is an argument to
    /// <c>NewGame</c>, and <see cref="StorytellerCatalogue"/> is what a host lists it from.
    ///
    /// <para/><b>The one seam limitation, stated rather than hidden.</b> <see cref="GodCommandOutcome"/> has
    /// no general "unknown handle" member — it has <see cref="GodCommandOutcome.UnknownEdict"/> and
    /// <see cref="GodCommandOutcome.UnknownSettlement"/>, neither of which is this — and it lives in
    /// <c>GodCommands.cs</c>, the shared file CLAUDE.md says to leave alone. An unknown defName therefore comes
    /// back as <see cref="GodCommandOutcome.Refused"/> naming the missing def in words, which is what
    /// <see cref="SetResearchProject"/>, <see cref="BuyFromTrader"/>, <see cref="DeclareWar"/> and
    /// <see cref="ApplyRoleToCivilization"/> all already do — so the seam stays consistent about it rather than
    /// this one command family reporting a stale handle differently from the other five. See
    /// <c>GodCommands.Trade.cs</c>, which reached the same conclusion one lane earlier.
    /// </summary>
    public static partial class GodCommands
    {
        /// <summary>
        /// Hands the civilization's story to <paramref name="defName"/> — the player choosing the rhythm they
        /// want the rest of the run to have.
        ///
        /// <para/>The change is wholesale and immediate: <see cref="Director.Storyteller.RebuildComps"/> runs,
        /// so from the next interval it is the new narrator's own comps deciding what fires and how often, and
        /// <see cref="StorytellerUtility.DefaultThreatPointsNow"/> starts reading the new narrator's
        /// days-passed curve. Rebuilding is safe at any moment because a <see cref="StorytellerComp"/> holds no
        /// state between calls — its own class doc says so, and everything a comp decides with comes from its
        /// props, the target and <see cref="Find"/>.
        ///
        /// <para/>What survives: the chronicle, the curated moments, the ledgers, adaptation, and anything
        /// already sitting in the <see cref="Director.IncidentQueue"/>. A narrator changing hands does not
        /// unmake the history they inherit, and an incident already scheduled still lands.
        /// </summary>
        public static GodCommandResult SetStoryteller(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return GodCommandResult.Refused("No storyteller id given.");
            }

            StorytellerDef? def = DefDatabase<StorytellerDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return GodCommandResult.Refused("No storyteller named '" + defName + "'.");
            }

            if (Find.CurrentGame == null)
            {
                return GodCommandResult.Refused(
                    "No game is running, so there is no story for a storyteller to tell.");
            }

            Storyteller storyteller = Find.Storyteller;
            if (storyteller.def == def)
            {
                return GodCommandResult.NoChange(def.LabelCap + " is already telling this story.");
            }

            storyteller.def = def;

            // Without this the change would be half a change — the days-passed curve would move and the
            // cadence would not, because the comps were built from the old def at construction. The
            // storyteller would still be firing Cassandra's rhythm under Randy's name, and the command would
            // have reported Done for it.
            storyteller.RebuildComps();

            return GodCommandResult.Done(def.LabelCap + " is now telling this civilization's story.");
        }

        /// <summary>
        /// Sets the difficulty the storyteller plays at — what the player is asking the world to cost them.
        ///
        /// <para/>Nothing is recomputed here and nothing needs to be: every consumer reads
        /// <see cref="Director.Storyteller.difficulty"/> at the moment it decides. The threat points of the
        /// next raid, the mood floor of every person in the settlement, what a harvest yields, how fast
        /// research goes and how often disease rolls all change from the next time each of them is asked, and
        /// none of them is asked by this method.
        ///
        /// <para/>Accumulated adaptation is deliberately kept. <see cref="StoryWatcher_Adaptation"/> is a
        /// record of how long this civilization has been left alone, which is true regardless of which preset
        /// is in force; clearing it on a difficulty change would hand a player a guaranteed quiet spell for
        /// touching the dial, and would make the dial worth touching for that rather than for the run it
        /// describes.
        /// </summary>
        public static GodCommandResult SetDifficulty(string defName)
        {
            if (string.IsNullOrEmpty(defName))
            {
                return GodCommandResult.Refused("No difficulty id given.");
            }

            DifficultyDef? def = DefDatabase<DifficultyDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return GodCommandResult.Refused("No difficulty named '" + defName + "'.");
            }

            if (Find.CurrentGame == null)
            {
                return GodCommandResult.Refused(
                    "No game is running, so there is no difficulty to play it at.");
            }

            Storyteller storyteller = Find.Storyteller;
            if (storyteller.difficulty == def)
            {
                return GodCommandResult.NoChange(def.LabelCap + " is already the difficulty in force.");
            }

            storyteller.difficulty = def;
            return GodCommandResult.Done("The story is now being told on " + def.LabelCap + ".");
        }
    }
}
