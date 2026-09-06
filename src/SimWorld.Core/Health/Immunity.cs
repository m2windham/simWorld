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
