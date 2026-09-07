using SimWorld.Defs;

namespace SimWorld.Director
{
    [DefOf]
    public static class IncidentCategoryDefOf
    {
        public static IncidentCategoryDef ThreatSmall = null!;
        public static IncidentCategoryDef ThreatBig = null!;
        public static IncidentCategoryDef Misc = null!;
        public static IncidentCategoryDef DiseaseHuman = null!;
    }

    [DefOf]
    public static class IncidentTargetTagDefOf
    {
        public static IncidentTargetTagDef Map_PlayerHome = null!;
        public static IncidentTargetTagDef World = null!;
    }

    [DefOf]
    public static class StorytellerDefOf
    {
        public static StorytellerDef Cassandra_Classic = null!;
    }

    [DefOf]
    public static class DifficultyDefOf
    {
        public static DifficultyDef Medium = null!;
    }

    [DefOf]
    public static class IncidentDefOf
    {
        public static IncidentDef RaidEnemy = null!;
        public static IncidentDef Disease_Flu = null!;
    }
}
