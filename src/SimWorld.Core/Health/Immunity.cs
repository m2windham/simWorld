using System;
using System.Collections.Generic;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Health
{
    /// <summary>Immunity progress against one disease (RimWorld: <c>RimWorld.ImmunityRecord</c>).</summary>
    public class ImmunityRecord : IExposable
    {
        public HediffDef hediffDef = null!;
        public float immunity;

        public ImmunityRecord()
        {
        }

        public ImmunityRecord(HediffDef hediffDef)
        {
            this.hediffDef = hediffDef ?? throw new ArgumentNullException(nameof(hediffDef));
        }

        public float ImmunityChangePerTick(Pawn pawn, bool sick)
        {
            HediffCompProperties_Immunizable? props = hediffDef.CompProps<HediffCompProperties_Immunizable>();
            if (props == null) return 0f;
            float perDay = sick ? props.immunityPerDaySick : props.immunityPerDayNotSick;
            float perTick = perDay / GenDate.TicksPerDay;
            if (sick) perTick *= pawn.ImmunityGainSpeed;
            return perTick;
        }

        public void ImmunityTick(Pawn pawn, bool sick)
        {
            immunity = GenMath.Clamp01(immunity + ImmunityChangePerTick(pawn, sick));
        }

        public void ExposeData()
        {
            HediffDef? d = hediffDef;
            Scribe_Defs.Look(ref d, "hediffDef");
            hediffDef = d!;
            Scribe_Values.Look(ref immunity, "immunity");
        }
    }

    /// <summary>
    /// Tracks immunity against every immunizable disease the pawn has met (RimWorld: <c>RimWorld.ImmunityHandler</c>).
    /// Immunity climbs while sick (faster with rest, per <see cref="Pawn.ImmunityGainSpeed"/>); at 1.0 the
    /// disease's severity starts falling instead of rising. Records fade once the disease is gone.
    /// </summary>
    public class ImmunityHandler : IExposable
    {
        private readonly Pawn pawn;
        private List<ImmunityRecord> immunityList = new List<ImmunityRecord>();

        public ImmunityHandler(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public IReadOnlyList<ImmunityRecord> Records => immunityList;

        /// <summary>
        /// How open this pawn is to catching <paramref name="diseaseDef"/> on <paramref name="part"/> right now
        /// (RimWorld: <c>ImmunityHandler.DiseaseContractChanceFactor</c>): 0 for a pawn that does not get sick,
        /// 0 if it already has this disease on this very part, and otherwise falling from 1 to 0 as its immunity
        /// to it climbs to 0.6. A pawn who is fighting one wound infection, or has just beaten one, has built
        /// the immunity that keeps the next wound from going septic; the infecter reads this at or below
        /// 0.001 as "no".
        /// </summary>
        public float DiseaseContractChanceFactor(HediffDef diseaseDef, BodyPartRecord? part = null)
        {
            if (diseaseDef == null) throw new ArgumentNullException(nameof(diseaseDef));
            if (!pawn.RaceProps.IsFlesh) return 0f;

            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def == diseaseDef && ReferenceEquals(hediffs[i].Part, part)) return 0f;
            }

            ImmunityRecord? record = GetImmunityRecord(diseaseDef);
            if (record != null) return GenMath.Lerp(1f, 0f, GenMath.Clamp01(record.immunity / 0.6f));
            return 1f;
        }

        public float GetImmunity(HediffDef def)
        {
            ImmunityRecord? record = GetImmunityRecord(def);
            return record?.immunity ?? 0f;
        }

        public ImmunityRecord? GetImmunityRecord(HediffDef def)
        {
            for (int i = 0; i < immunityList.Count; i++)
            {
                if (immunityList[i].hediffDef == def) return immunityList[i];
            }
            return null;
        }

        /// <summary>
        /// <see cref="GetImmunityRecord"/>, but creates a fresh zero-immunity record rather than returning
        /// null. <see cref="ImmunityHandlerTick"/> is the only other place a record gets created, and it never
        /// runs for a pawn at <see cref="PawnTier.Interval"/> — so <see cref="AbstractDiseaseResolver"/>, which
        /// does have to track immunity for a hediff caught at that tier, needs somewhere to keep the progress
        /// it computes. Idempotent: a second call for the same def returns the same record the first call made.
        /// </summary>
        public ImmunityRecord EnsureRecord(HediffDef def)
        {
            ImmunityRecord? existing = GetImmunityRecord(def);
            if (existing != null) return existing;
            var created = new ImmunityRecord(def);
            immunityList.Add(created);
            return created;
        }

        public void ImmunityHandlerTick()
        {
            List<Hediff> hediffs = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                if (hediffs[i].def.HasComp(typeof(HediffComp_Immunizable)) && GetImmunityRecord(hediffs[i].def) == null)
                {
                    immunityList.Add(new ImmunityRecord(hediffs[i].def));
                }
            }
            for (int i = immunityList.Count - 1; i >= 0; i--)
            {
                ImmunityRecord record = immunityList[i];
                bool sick = pawn.health.hediffSet.HasHediff(record.hediffDef);
                record.ImmunityTick(pawn, sick);
                if (!sick && record.immunity <= 0f) immunityList.RemoveAt(i);
            }
        }

        public void ExposeData()
        {
            List<ImmunityRecord>? list = immunityList;
            Scribe_Collections.Look(ref list, "immunityList", LookMode.Deep);
            immunityList = list ?? new List<ImmunityRecord>();
        }
    }
}
