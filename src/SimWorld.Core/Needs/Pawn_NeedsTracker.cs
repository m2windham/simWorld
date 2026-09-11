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

        /// <summary>
        /// Whether this pawn is one of the people this need is for (RimWorld:
        /// <c>Pawn_NeedsTracker.ShouldHaveNeed</c>, whose applicability flags this now consults).
        ///
        /// <para/><b>What was wrong.</b> Only <see cref="NeedDef.minIntelligence"/> and
        /// <c>RaceProps.needsRest</c> were asked, so every other flag a <see cref="NeedDef"/> carries was
        /// ignored and every pawn was given every need its species could hold — a captured raider kept a
        /// recreation need nobody would ever let them satisfy, and it decayed to nothing and dragged their
        /// mood down for as long as they were held.
        ///
        /// <para/><b>Order matters and is RimWorld's.</b> The species test comes first (it is the cheapest and
        /// the most absolute), then the who-owns-them tests. Each of the three flags is asked only when the
        /// Def actually sets it, which keeps <see cref="Pawns.PawnUtility.IsPrisoner"/>'s scan of the
        /// factions' prisoner lists off the path of every need of every pawn ever built: exactly one shipped
        /// need sets a prisoner flag, so exactly one lookup happens per pawn.
        ///
        /// <para/><b>This is re-asked, not asked once.</b> A pawn is built before it is given a faction
        /// (<c>PawnGenerator</c> constructs, then the generator or the founder enfactions), so a colonist
        /// tested at construction is factionless and would fail every one of these. <c>Pawn.faction</c>'s
        /// setter re-runs <see cref="AddOrRemoveNeedsAsAppropriate"/> for that reason — RimWorld does the
        /// same from <c>Pawn.SetFaction</c> — and so does capture and release
        /// (<c>Factions.CaptureUtility</c>), which is the other way the answers here change during a life.
        /// </summary>
        public bool ShouldHaveNeed(NeedDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (pawn.RaceProps.intelligence < def.minIntelligence) return false;
            if (typeof(Need_Rest).IsAssignableFrom(def.needClass) && !pawn.RaceProps.needsRest) return false;

            // RimWorld: "colonist, or a prisoner of the colony" — the people living in the settlement, whether
            // or not by choice. A pawn passing through with a faction of its own is neither.
            if (def.colonistAndPrisonersOnly
                && !PawnUtility.IsColonist(pawn) && !PawnUtility.IsPrisonerOfColony(pawn)) return false;

            if (def.colonistsOnly && !PawnUtility.IsColonist(pawn)) return false;

            if (def.neverOnPrisoner && PawnUtility.IsPrisoner(pawn)) return false;

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
