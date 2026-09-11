using System;
using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Needs
{
    /// <summary>
    /// The needs a pawn currently has (RimWorld: <c>RimWorld.Pawn_NeedsTracker</c>). Which NeedDefs apply
    /// depends on species intelligence and flags; instances are created from <see cref="NeedDef.needClass"/>.
    /// </summary>
    public class Pawn_NeedsTracker : IExposable
    {
        private readonly Pawn pawn;
        private List<Need> needs = new List<Need>();

        public Need_Mood? mood;
        public Need_Food? food;
        public Need_Rest? rest;
        public Need_Joy? joy;

        public Pawn_NeedsTracker(Pawn pawn)
        {
            this.pawn = pawn ?? throw new ArgumentNullException(nameof(pawn));
        }

        public IReadOnlyList<Need> AllNeeds => needs;

        /// <summary>Runs each need's interval on this pawn's 150-tick slot.</summary>
        public void NeedsTrackerTick()
        {
            if (!pawn.IsHashIntervalTick(Need.IntervalTicks)) return;
            for (int i = 0; i < needs.Count; i++)
            {
                needs[i].NeedInterval();
            }
        }

        public T? TryGetNeed<T>() where T : Need
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (needs[i] is T typed) return typed;
            }
            return null;
        }

        public Need? TryGetNeed(NeedDef def)
        {
            for (int i = 0; i < needs.Count; i++)
            {
                if (needs[i].def == def) return needs[i];
            }
            return null;
        }

        public bool ShouldHaveNeed(NeedDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (pawn.RaceProps.intelligence < def.minIntelligence) return false;
            if (typeof(Need_Rest).IsAssignableFrom(def.needClass) && !pawn.RaceProps.needsRest) return false;
            return true;
        }

        /// <summary>Adds missing needs and drops ones the pawn should no longer have; uses the global Def database.</summary>
        public void AddOrRemoveNeedsAsAppropriate()
        {
            foreach (NeedDef def in DefDatabase<NeedDef>.AllDefsListForReading)
            {
                bool has = TryGetNeed(def) != null;
                bool should = ShouldHaveNeed(def);
                if (should && !has) AddNeed(def);
                else if (!should && has) RemoveNeed(def);
            }
        }

        public Need AddNeed(NeedDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (TryGetNeed(def) != null) throw new InvalidOperationException(pawn + " already has need " + def.defName + ".");
            var need = (Need)Activator.CreateInstance(def.needClass, pawn)!;
            need.def = def;
            needs.Add(need);
            need.SetInitialLevel();
            BindDirectNeedFields();
            return need;
        }

        public void RemoveNeed(NeedDef def)
        {
            Need? need = TryGetNeed(def);
            if (need == null) return;
            needs.Remove(need);
            BindDirectNeedFields();
        }

        /// <summary>
        /// The pawn crossed into a new life stage, so any need whose ceiling is stage-scaled now has a
        /// different one (<see cref="Need_Food.MaxLevel"/> is the only such need today). Re-clamping through
        /// the <see cref="Need.CurLevel"/> setter is the whole job: growing up raises the ceiling and leaves
        /// the level untouched — that is what makes a newly-grown pawn hungry rather than resetting it — and
        /// a ceiling that dropped pulls the level down with it, so <see cref="Need.CurLevelPercentage"/> can
        /// never report more than 100% of a stomach the pawn no longer has.
        /// </summary>
        public void Notify_LifeStageStarted()
        {
            for (int i = 0; i < needs.Count; i++)
            {
                // Through the setter on purpose: that is where the clamp to [0, MaxLevel] lives, and
                // MaxLevel is what just changed.
                float level = needs[i].CurLevel;
                needs[i].CurLevel = level;
            }
        }

        private void BindDirectNeedFields()
        {
            mood = TryGetNeed<Need_Mood>();
            food = TryGetNeed<Need_Food>();
            rest = TryGetNeed<Need_Rest>();
            joy = TryGetNeed<Need_Joy>();
        }

        public void ExposeData()
        {
            List<Need>? list = needs;
            Scribe_Collections.Look(ref list, "needs", LookMode.Deep, pawn);
            needs = list ?? new List<Need>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                needs.RemoveAll(n => n == null || n.def == null);
                BindDirectNeedFields();
            }
        }
    }
}
