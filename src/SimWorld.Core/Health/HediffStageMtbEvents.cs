using System.Collections.Generic;

using SimWorld.MindState;
using SimWorld.Pawns;
using SimWorld.Sim;
using SimWorld.Thoughts;

namespace SimWorld.Health
{
    /// <summary>
    /// The two mean-time stage effects a <see cref="HediffStage"/> declares and nothing used to read:
    /// <see cref="HediffStage.mentalBreakMtbDays"/> and <see cref="HediffStage.forgetMemoryThoughtMtbDays"/>
    /// (defs.wiringaudit.untouched.gaps). RimWorld rolls both from <c>Pawn_HealthTracker.HealthTick</c>, once
    /// per hediff every 60 ticks, with <c>Rand.MTBEventOccurs(mtbDays, 60000f, 60f)</c>; the call shapes below
    /// are recalled rather than checked against source (the break is drawn from every break that can occur
    /// for the pawn, weighted by commonality, and started as not mood-caused with the hediff as the reason;
    /// the forgotten memory is any one memory the pawn carries), and the tests pin behaviour, not odds.
    ///
    /// <para/>No shipped hediff sets either field (both default to -1), so this draws nothing from
    /// <see cref="Rand.Current"/> in any shipped game and leaves every seeded run where it was.
    /// </summary>
    public static class HediffStageMtbEvents
    {
        /// <summary>RimWorld's check interval for these rolls.</summary>
        public const int CheckIntervalTicks = 60;

        private static readonly List<MentalBreakDef> tmpBreaks = new List<MentalBreakDef>();

        /// <summary>Called from <see cref="Pawn_HealthTracker.HealthTick"/>.</summary>
        public static void HealthTickInterval(Pawn pawn, List<Hediff> hediffs)
        {
            if (!pawn.IsHashIntervalTick(CheckIntervalTicks)) return;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff hediff = hediffs[i];
                HediffStage? stage = hediff.CurStage;
                if (stage == null) continue;

                if (stage.mentalBreakMtbDays > 0f
                    && Rand.Current.MTBEventOccurs(stage.mentalBreakMtbDays, GenDate.TicksPerDay, CheckIntervalTicks))
                {
                    TryRandomMentalBreak(pawn, hediff.Label);
                }

                if (stage.forgetMemoryThoughtMtbDays > 0f && pawn.needs?.mood != null
                    && Rand.Current.MTBEventOccurs(stage.forgetMemoryThoughtMtbDays, GenDate.TicksPerDay, CheckIntervalTicks))
                {
                    TryForgetMemory(pawn);
                }
            }
        }

        private static bool TryRandomMentalBreak(Pawn pawn, string reason)
        {
            if (pawn.InMentalState || pawn.Downed) return false;
            tmpBreaks.Clear();
            foreach (MentalBreakDef def in Defs.DefDatabase<MentalBreakDef>.AllDefsListForReading)
            {
                if (def.Worker.BreakCanOccur(pawn)) tmpBreaks.Add(def);
            }
            if (!GenCollection.TryRandomElementByWeight(tmpBreaks, d => d.Worker.CommonalityFor(pawn), Rand.Current, out MentalBreakDef chosen))
            {
                return false;
            }
            return chosen.Worker.TryStart(pawn, reason, causedByMood: false);
        }

        private static bool TryForgetMemory(Pawn pawn)
        {
            MemoryThoughtHandler memories = pawn.needs!.mood!.thoughts.memories;
            IReadOnlyList<Thought_Memory> all = memories.Memories;
            if (all.Count == 0) return false;
            memories.RemoveMemory(all[Rand.Current.Range(0, all.Count)]);
            return true;
        }
    }
}
