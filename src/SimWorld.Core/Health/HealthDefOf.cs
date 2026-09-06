using SimWorld.Defs;

namespace SimWorld.Health
{
    [DefOf]
    public static class HediffDefOf
    {
        public static HediffDef MissingBodyPart = null!;
        public static HediffDef BloodLoss = null!;
        public static HediffDef Malnutrition = null!;
        public static HediffDef WoundInfection = null!;
    }

    [DefOf]
    public static class PawnCapacityDefOf
    {
        public static PawnCapacityDef Consciousness = null!;
        public static PawnCapacityDef Moving = null!;
        public static PawnCapacityDef Manipulation = null!;
        public static PawnCapacityDef Sight = null!;
        public static PawnCapacityDef Hearing = null!;
        public static PawnCapacityDef Talking = null!;
        public static PawnCapacityDef Eating = null!;
        public static PawnCapacityDef Breathing = null!;
        public static PawnCapacityDef BloodPumping = null!;
        public static PawnCapacityDef BloodFiltration = null!;
        public static PawnCapacityDef Metabolism = null!;
    }

    [DefOf]
    public static class BodyPartTagDefOf
    {
        public static BodyPartTagDef ConsciousnessSource = null!;
        public static BodyPartTagDef BloodPumpingSource = null!;
        public static BodyPartTagDef BloodFiltrationSource = null!;
        public static BodyPartTagDef BloodFiltrationKidney = null!;
        public static BodyPartTagDef BloodFiltrationLiver = null!;
        public static BodyPartTagDef BreathingSource = null!;
        public static BodyPartTagDef BreathingSourceCage = null!;
        public static BodyPartTagDef BreathingPathway = null!;
        public static BodyPartTagDef EatingSource = null!;
        public static BodyPartTagDef EatingPathway = null!;
        public static BodyPartTagDef TalkingSource = null!;
        public static BodyPartTagDef TalkingPathway = null!;
        public static BodyPartTagDef SightSource = null!;
        public static BodyPartTagDef HearingSource = null!;
        public static BodyPartTagDef MetabolismSource = null!;
        public static BodyPartTagDef MovingLimbCore = null!;
        public static BodyPartTagDef MovingLimbSegment = null!;
        public static BodyPartTagDef MovingLimbDigit = null!;
        public static BodyPartTagDef ManipulationLimbCore = null!;
        public static BodyPartTagDef ManipulationLimbSegment = null!;
        public static BodyPartTagDef ManipulationLimbDigit = null!;
        public static BodyPartTagDef Pelvis = null!;
        public static BodyPartTagDef Spine = null!;
    }

    [DefOf]
    public static class DamageDefOf
    {
        public static DamageDef Cut = null!;
        public static DamageDef Blunt = null!;
        public static DamageDef Bullet = null!;
        public static DamageDef Bite = null!;
        public static DamageDef Burn = null!;
        public static DamageDef Stab = null!;
    }
}
