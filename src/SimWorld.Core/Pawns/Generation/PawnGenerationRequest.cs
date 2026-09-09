using System;
using SimWorld.Factions;
using SimWorld.Pawns.Genes;

namespace SimWorld.Pawns.Generation
{
    /// <summary>
    /// Everything a call to <see cref="PawnGenerator.GeneratePawn"/> can pin down or leave to chance (RimWorld:
    /// <c>RimWorld.PawnGenerationRequest</c>, trimmed to what SimWorld's generator reads — no dead/downed
    /// knobs yet). <see cref="Faction"/> is what <see cref="Generation.PawnWeaponGenerator"/> reads to cap a
    /// generated raider's gear at its own faction's tech level; a request with none generated is gear-ungated.
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

        /// <summary>The faction this pawn belongs to, if any (RimWorld: <c>PawnGenerationRequest.Faction</c>).</summary>
        public Faction? Faction { get; }

        /// <summary>
        /// The germline this pawn is generated with (RimWorld: <c>PawnGenerationRequest</c> does not carry this
        /// directly — RimWorld resolves a pawn's xenotype from its <c>PawnKindDef</c>/faction/xenotype-chance
        /// tables instead; this port has none of those tables yet, so the request takes it explicitly). Null
        /// (the default) means Baseliner: no genes at all, and — deliberately — <see cref="Generation.PawnGenerator"/>
        /// then applies none, consuming no extra <see cref="Sim.Rand"/> calls, so a caller that never asks for a
        /// xenotype gets byte-for-byte the same generation this port had before genes existed. See
        /// <c>PawnGenerator.GenerateInternal</c>'s own comment on why gene application sits downstream of every
        /// other roll.
        /// </summary>
        public XenotypeDef? Xenotype { get; }

        public PawnGenerationRequest(
            PawnKindDef kindDef,
            Gender? fixedGender = null,
            float? fixedBiologicalAge = null,
            float? fixedChronologicalAge = null,
            bool mustBeCapableOfViolence = false,
            bool forceGenerateNewPawn = false,
            bool newborn = false,
            Faction? faction = null,
            XenotypeDef? xenotype = null)
        {
            KindDef = kindDef ?? throw new ArgumentNullException(nameof(kindDef));
            FixedGender = fixedGender;
            FixedBiologicalAge = fixedBiologicalAge;
            FixedChronologicalAge = fixedChronologicalAge;
            MustBeCapableOfViolence = mustBeCapableOfViolence;
            ForceGenerateNewPawn = forceGenerateNewPawn;
            Newborn = newborn;
            Faction = faction;
            Xenotype = xenotype;
        }
    }
}
