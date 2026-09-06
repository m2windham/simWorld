using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Work
{
    /// <summary>
    /// A pawn's skills (RimWorld: <c>RimWorld.Pawn_SkillTracker</c>): one <see cref="SkillRecord"/> per loaded
    /// <see cref="SkillDef"/>, in <see cref="SkillDef.listOrder"/> order. Decays unused skills every 200 ticks
    /// and resets each record's daily XP counter at the start of a new game day.
    /// </summary>
    public class Pawn_SkillTracker : IExposable
    {
        public const int IntervalTicks = 200;

        private readonly Pawn pawn;
        public List<SkillRecord> skills = new List<SkillRecord>();

        private int lastResetDay = -1;

        public Pawn_SkillTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
            foreach (SkillDef def in SortedByListOrder())
            {
                skills.Add(new SkillRecord(pawn, def));
            }
        }

        public SkillRecord? GetSkill(SkillDef def)
        {
            for (int i = 0; i < skills.Count; i++)
            {
                if (skills[i].def == def) return skills[i];
            }
            return null;
        }

        public void Learn(SkillDef def, float xp, bool direct = false) => GetSkill(def)?.Learn(xp, direct);

        public Passion MaxPassionOfRelevantSkillsFor(WorkTypeDef workType)
        {
            if (workType == null) throw new ArgumentNullException(nameof(workType));
            Passion max = Passion.None;
            if (workType.relevantSkills == null) return max;
            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillRecord? record = GetSkill(workType.relevantSkills[i]);
                if (record != null && record.passion > max) max = record.passion;
            }
            return max;
        }

        /// <summary>Average level across the work type's relevant skills; 3 (the "average" default) when it has none.</summary>
        public float AverageOfRelevantSkillsFor(WorkTypeDef workType)
        {
            if (workType == null) throw new ArgumentNullException(nameof(workType));
            if (workType.relevantSkills == null || workType.relevantSkills.Count == 0) return 3f;

            float total = 0f;
            int count = 0;
            for (int i = 0; i < workType.relevantSkills.Count; i++)
            {
                SkillRecord? record = GetSkill(workType.relevantSkills[i]);
                if (record == null) continue;
                total += record.Level;
                count++;
            }
            return count == 0 ? 3f : total / count;
        }

        public void Notify_SkillDisablesChanged()
        {
            for (int i = 0; i < skills.Count; i++) skills[i].Notify_SkillDisablesChanged();
        }

        /// <summary>Runs every tick: cheap day-boundary check, plus the 200-tick decay pass on this pawn's hash slot.</summary>
        public void SkillsTick()
        {
            int today = GenDate.DaysPassedAt(Find.TickManager.TicksGame);
            if (lastResetDay < 0)
            {
                lastResetDay = today;
            }
            else if (today != lastResetDay)
            {
                lastResetDay = today;
                for (int i = 0; i < skills.Count; i++) skills[i].xpSinceMidnight = 0f;
            }

            if (pawn.IsHashIntervalTick(IntervalTicks))
            {
                for (int i = 0; i < skills.Count; i++) skills[i].Interval();
            }
        }

        private static IEnumerable<SkillDef> SortedByListOrder()
        {
            var list = new List<SkillDef>(DefDatabase<SkillDef>.AllDefsListForReading);
            list.Sort((a, b) => a.listOrder.CompareTo(b.listOrder));
            return list;
        }

        public void ExposeData()
        {
            List<SkillRecord>? list = skills;
            Scribe_Collections.Look(ref list, "skills", LookMode.Deep, pawn);
            skills = list ?? new List<SkillRecord>();
            Scribe_Values.Look(ref lastResetDay, "lastResetDay", -1);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                foreach (SkillDef def in SortedByListOrder())
                {
                    if (GetSkill(def) == null) skills.Add(new SkillRecord(pawn, def));
                }
            }
        }
    }
}
