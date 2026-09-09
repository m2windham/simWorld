using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Work;

namespace SimWorld.Stats
{
    /// <summary>
    /// One skill-driven term in a <see cref="StatDef"/>'s <c>skillNeedOffsets</c>/<c>skillNeedFactors</c> list
    /// (RimWorld: <c>RimWorld.SkillNeed</c>), read by <see cref="StatWorker.GetValueUnfinalized"/> off the
    /// pawn's own <see cref="SkillRecord"/> level for <see cref="skill"/>. RimWorld ships two concrete shapes
    /// and both are ported here: <see cref="SkillNeed_Direct"/> (an explicit value per level) and
    /// <see cref="SkillNeed_BaseBonus"/> (a flat value plus a per-level bonus). Selected in content the way
    /// this repo's other list-of-worker fields already are (<c>StatPart</c>, <c>HediffComp</c>): a bare
    /// <c>&lt;li Class="SimWorld.Stats.SkillNeed_Direct"&gt;</c>.
    /// <para/>
    /// A skill a trait or backstory disables (<see cref="Pawn.WorkTagIsDisabled"/>) reads as level 0 here for
    /// free: <see cref="SkillRecord.Level"/> itself already collapses to 0 when
    /// <see cref="SkillRecord.TotallyDisabled"/>, so a pawn barred from Medicine gets the un-boosted base
    /// <c>MedicalTendQuality</c> rather than a skill-scaled one, with no extra check needed on this side.
    /// </summary>
    public abstract class SkillNeed
    {
        /// <summary>The skill this term reads (RimWorld: <c>SkillNeed.skill</c>).</summary>
        public SkillDef skill = null!;

        public abstract float ValueFor(Pawn pawn);

        /// <summary>The pawn's level in <see cref="skill"/>, or 0 if it has no record for it at all. In this
        /// port every pawn's <see cref="Pawn_SkillTracker"/> carries one record per loaded <see cref="SkillDef"/>
        /// (see <see cref="Pawn_SkillTracker"/>'s constructor), so the fallback never actually triggers today —
        /// kept defensive rather than assuming, the same caution <c>JobDriver_ConstructFinishFrame</c> already
        /// takes reading a skill level directly.</summary>
        protected static int LevelFor(Pawn pawn, SkillDef skill) => pawn.skills?.GetSkill(skill)?.Level ?? 0;
    }

    /// <summary>
    /// Looks the value up directly from a table, one entry per skill level (RimWorld:
    /// <c>RimWorld.SkillNeed_Direct</c>). RimWorld's own content always supplies all 21 (0..<see cref="SkillRecord.MaxLevel"/>)
    /// entries; a shorter table here clamps to its last entry instead of throwing, so a deliberately truncated
    /// table ("everything past level 10 behaves the same") still means what it says rather than needing every
    /// level spelled out.
    /// </summary>
    public class SkillNeed_Direct : SkillNeed
    {
        public List<float> valuesPerLevel = new List<float>();

        public override float ValueFor(Pawn pawn)
        {
            if (valuesPerLevel.Count == 0) return 0f;
            int level = LevelFor(pawn, skill);
            int index = level < valuesPerLevel.Count ? level : valuesPerLevel.Count - 1;
            return valuesPerLevel[index];
        }
    }

    /// <summary>
    /// <see cref="baseValue"/> at level 0, plus <see cref="bonusPerLevel"/> for every level above it (RimWorld:
    /// <c>RimWorld.SkillNeed_BaseBonus</c>).
    /// </summary>
    public class SkillNeed_BaseBonus : SkillNeed
    {
        public float baseValue;
        public float bonusPerLevel;

        public override float ValueFor(Pawn pawn) => baseValue + bonusPerLevel * LevelFor(pawn, skill);
    }
}
