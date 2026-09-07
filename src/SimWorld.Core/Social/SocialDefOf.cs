using SimWorld.Defs;
using SimWorld.MindState;
using SimWorld.Thoughts;

namespace SimWorld.Social
{
    /// <summary>Non-family relation kinds content must define (RimWorld: <c>RimWorld.PawnRelationDefOf</c>).
    /// The family kinds (<c>Spouse</c>, <c>Parent</c>, <c>Child</c>, <c>Sibling</c>) are Defs too — see
    /// <c>Data/Core/Defs/PawnRelationDefs</c> — but nothing in code needs them by name; every relation is
    /// looked up generically through <see cref="DefDatabase{T}.AllDefsListForReading"/> in
    /// <see cref="SocialUtility.RelationOpinionOffset"/>, so only the ones code constructs directly need a
    /// binding here.</summary>
    [DefOf]
    public static class PawnRelationDefOf
    {
        public static PawnRelationDef Friend = null!;
        public static PawnRelationDef Rival = null!;
        public static PawnRelationDef Lover = null!;
        public static PawnRelationDef ExSpouse = null!;
    }

    [DefOf]
    public static class InteractionDefOf
    {
        public static InteractionDef Chitchat = null!;
        public static InteractionDef DeepTalk = null!;
        public static InteractionDef Insult = null!;
        public static InteractionDef Slight = null!;
    }

    /// <summary>Social memory ThoughtDefs code constructs directly. <c>Insulted</c> already shipped with the
    /// mood system (<c>Data/Core/Defs/ThoughtDefs/Thoughts_Memories.xml</c>) — this reuses it rather than
    /// adding a second "you got insulted" thought.</summary>
    [DefOf]
    public static class SocialThoughtDefOf
    {
        public static ThoughtDef HadChitchat = null!;
        public static ThoughtDef HadDeepTalk = null!;
        public static ThoughtDef Insulted = null!;
        public static ThoughtDef WasSlighted = null!;
    }

    /// <summary>The mental state a social fight starts (RimWorld: <c>MentalStateDefOf.SocialFighting</c>) —
    /// reuses <see cref="MindState.MentalStateHandler"/> directly rather than a parallel fight system.</summary>
    [DefOf]
    public static class SocialMentalStateDefOf
    {
        public static MentalStateDef SocialFighting = null!;
    }
}
