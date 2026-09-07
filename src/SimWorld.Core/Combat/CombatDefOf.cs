using SimWorld.Defs;
using SimWorld.Health;

namespace SimWorld.Combat
{
    [DefOf]
    public static class DamageArmorCategoryDefOf
    {
        public static DamageArmorCategoryDef Sharp = null!;
        public static DamageArmorCategoryDef Blunt = null!;
        public static DamageArmorCategoryDef Heat = null!;
    }

    [DefOf]
    public static class ToolCapacityDefOf
    {
        public static ToolCapacityDef Blunt = null!;
        public static ToolCapacityDef Cut = null!;
        public static ToolCapacityDef Stab = null!;
    }
}
