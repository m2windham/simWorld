using System;

namespace SimWorld.Pawns.Generation
{
    /// <summary>
    /// Everything a call to <see cref="PawnGenerator.GeneratePawn"/> can pin down or leave to chance (RimWorld:
    /// <c>RimWorld.PawnGenerationRequest</c>, trimmed to what SimWorld's generator reads — no faction, gear or
    /// dead/downed knobs yet).
    /// </summary>
    public readonly struct PawnGenerationRequest
    {
        public PawnKindDef KindDef { get; }
        public Gender? FixedGender { get; }
        public float? FixedBiologicalAge { get; }
        public float? FixedChronologicalAge { get; }
        public bool MustBeCapableOfViolence { get; }
        public bool ForceGenerateNewPawn { get; }

        /// <summary>Generates a newborn: age 0, no backstories, no traits, name only.</summary>
        public bool Newborn { get; }

        public PawnGenerationRequest(
            PawnKindDef kindDef,
            Gender? fixedGender = null,
            float? fixedBiologicalAge = null,
            float? fixedChronologicalAge = null,
            bool mustBeCapableOfViolence = false,
            bool forceGenerateNewPawn = false,
            bool newborn = false)
        {
            KindDef = kindDef ?? throw new ArgumentNullException(nameof(kindDef));
            FixedGender = fixedGender;
            FixedBiologicalAge = fixedBiologicalAge;
            FixedChronologicalAge = fixedChronologicalAge;
            MustBeCapableOfViolence = mustBeCapableOfViolence;
            ForceGenerateNewPawn = forceGenerateNewPawn;
            Newborn = newborn;
        }
    }
}
