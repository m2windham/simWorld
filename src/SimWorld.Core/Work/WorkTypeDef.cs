using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;

namespace SimWorld.Work
{
    /// <summary>
    /// One row of the work priority grid (RimWorld: <c>Verse.WorkTypeDef</c>): firefighting, doctoring, hauling,
    /// and so on. <see cref="WorkGivers"/> is resolved lazily rather than in <see cref="Def.ResolveReferences"/>
    /// because load order between WorkTypeDefs and WorkGiverDefs is not guaranteed.
    /// </summary>
    public class WorkTypeDef : Def
    {
        public string? labelShort;
        public string? gerundLabel;
        public string? verb;

        /// <summary>Higher runs earlier in the default (non-priority-grid) work-giver order.</summary>
        public int naturalPriority;

        /// <summary>Starts at priority 3 on a freshly initialized <see cref="Pawn_WorkSettings"/> even without being asked.</summary>
        public bool alwaysStartActive;

        public bool requireCapableColonist = true;

        public WorkTags workTags = WorkTags.None;

        public List<SkillDef>? relevantSkills;

        public bool disabledForSlaves;

        private IReadOnlyList<WorkGiverDef>? cachedWorkGivers;

        /// <summary>Every <see cref="WorkGiverDef"/> targeting this work type, higher <c>priorityInType</c> first.</summary>
        public IReadOnlyList<WorkGiverDef> WorkGivers
        {
            get
            {
                if (cachedWorkGivers == null)
                {
                    cachedWorkGivers = DefDatabase<WorkGiverDef>.AllDefsListForReading
                        .Where(g => g.workType == this)
                        .OrderByDescending(g => g.priorityInType)
                        .ToList();
                }
                return cachedWorkGivers;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            cachedWorkGivers = null;
        }
    }

    /// <summary>The 20 core work types, bound by defName after content loads.</summary>
    [DefOf]
    public static class WorkTypeDefOf
    {
        public static WorkTypeDef Firefighter = null!;
        public static WorkTypeDef Patient = null!;
        public static WorkTypeDef Doctor = null!;
        public static WorkTypeDef PatientBedRest = null!;
        public static WorkTypeDef BasicWorker = null!;
        public static WorkTypeDef Warden = null!;
        public static WorkTypeDef Handling = null!;
        public static WorkTypeDef Cooking = null!;
        public static WorkTypeDef Hunting = null!;
        public static WorkTypeDef Construction = null!;
        public static WorkTypeDef Growing = null!;
        public static WorkTypeDef Mining = null!;
        public static WorkTypeDef PlantCutting = null!;
        public static WorkTypeDef Smithing = null!;
        public static WorkTypeDef Tailoring = null!;
        public static WorkTypeDef Art = null!;
        public static WorkTypeDef Crafting = null!;
        public static WorkTypeDef Hauling = null!;
        public static WorkTypeDef Cleaning = null!;
        public static WorkTypeDef Research = null!;
    }

    /// <summary>The 12 core skills, bound by defName after content loads.</summary>
    [DefOf]
    public static class SkillDefOf
    {
        public static SkillDef Shooting = null!;
        public static SkillDef Melee = null!;
        public static SkillDef Construction = null!;
        public static SkillDef Mining = null!;
        public static SkillDef Cooking = null!;
        public static SkillDef Plants = null!;
        public static SkillDef Animals = null!;
        public static SkillDef Crafting = null!;
        public static SkillDef Artistic = null!;
        public static SkillDef Medicine = null!;
        public static SkillDef Social = null!;
        public static SkillDef Intellectual = null!;
    }
}
