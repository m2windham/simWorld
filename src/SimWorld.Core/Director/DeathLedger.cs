using System;
using System.Collections.Generic;

using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Director
{
    /// <summary>
    /// How many of this civilization's people have died, and of what, for as long as the game lasts.
    ///
    /// <para/><b>Why a counter and not the chronicle.</b> <see cref="Storyteller.Chronicle"/> is a narrative
    /// device and is capped at <see cref="Storyteller.ChronicleCapacity"/> entries — the oldest fall off. That
    /// is right for a record the player reads and wrong for a measurement, because a run long enough to be
    /// interesting is exactly the run whose early deaths have been evicted. Counting the dead from the
    /// chronicle would quietly under-report in proportion to how much history there is, which is the worst
    /// possible failure mode for an instrument. This holds one integer per cause, so a century costs the same
    /// as a day.
    ///
    /// <para/><b>Why it is not reconstructable after the fact.</b> The other two candidates fail too.
    /// <c>Settlement.PruneDeadCitizens</c> removes the dead from the roster on every sync, so a naive
    /// head-count reports zero deaths however many there were. <c>Corpse</c> things are only made for a pawn
    /// that was on a map (<c>CorpseMaker</c>), so every death at Interval or Statistical tier leaves no body
    /// to count. A death has to be recorded as it happens or it is gone.
    ///
    /// <para/><b>Citizens only.</b> Raiders and animals reach the same funnel and are deliberately not
    /// counted: this measures pressure on the civilization the player is responsible for, and a repelled
    /// raid's casualties are the attacker's problem. Membership is asked with
    /// <c>StorytellerPawnEvents.IsCivilizationMember(pawn, allowDead: true)</c> — the same question the
    /// adaptation charge next to it asks, so the two can never disagree about whose death it was.
    /// </summary>
    public sealed class DeathLedger : IExposable
    {
        private static readonly int CauseCount = Enum.GetValues(typeof(DeathCause)).Length;

        private readonly int[] counts = new int[CauseCount];

        /// <summary>How many have died of <paramref name="cause"/>.</summary>
        public int this[DeathCause cause] => counts[(int)cause];

        /// <summary>Every recorded death, whatever the cause.</summary>
        public int Total
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < counts.Length; i++) sum += counts[i];
                return sum;
            }
        }

        /// <summary>One death. Called once per pawn: <c>Pawn_HealthTracker.Kill</c> returns early for a pawn
        /// already dead, so the funnel this sits on cannot fire twice for the same body and the ledger needs
        /// no record of who it has already counted.</summary>
        public void Record(DeathCause cause) => counts[(int)cause]++;

        /// <summary>The whole vector, for a caller comparing two runs. Ordered by the enum, so index
        /// <c>i</c> is <c>(DeathCause)i</c> in every snapshot.</summary>
        public IReadOnlyList<int> Snapshot() => (int[])counts.Clone();

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("deaths ").Append(Total).Append(" (");
            bool first = true;
            for (int i = 0; i < counts.Length; i++)
            {
                if (counts[i] == 0) continue;
                if (!first) sb.Append(", ");
                sb.Append((DeathCause)i).Append(' ').Append(counts[i]);
                first = false;
            }
            if (first) sb.Append("none");
            return sb.Append(')').ToString();
        }

        /// <summary>
        /// Saved as a list in enum order rather than a keyed map, so a save written before a
        /// <see cref="DeathCause"/> gained a member still loads: the copy is bounded by both lengths and any
        /// cause the save did not know about stays at zero.
        /// </summary>
        public void ExposeData()
        {
            List<int>? list = new List<int>(counts);
            Scribe_Collections.Look(ref list, "countsByCause", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars && list != null)
            {
                Array.Clear(counts, 0, counts.Length);
                int n = Math.Min(list.Count, counts.Length);
                for (int i = 0; i < n; i++) counts[i] = list[i];
            }
        }
    }
}
