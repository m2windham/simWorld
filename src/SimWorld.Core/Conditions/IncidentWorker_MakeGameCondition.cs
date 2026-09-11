using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Letters;
using SimWorld.Sim;

namespace SimWorld.Conditions
{
    /// <summary>
    /// The incident that starts a condition (RimWorld: <c>RimWorld.IncidentWorker_MakeGameCondition</c>).
    /// Named as the <c>workerClass</c> of <c>HeatWave</c>, <c>ColdSnap</c> and <c>Flashstorm</c>, replacing
    /// the <c>IncidentWorker_Placeholder</c> all three shipped with — three incidents that were in the
    /// storyteller's repertoire, could be selected, fired, and changed nothing.
    ///
    /// <para/><b>Which condition it makes.</b> Not <c>IncidentDef.gameCondition</c>: the link is inverted and
    /// lives on <see cref="GameConditionDef.incident"/> instead, so that adding this system needed no new
    /// field on <c>Director/IncidentDefs.cs</c> (CLAUDE.md's "prefer inverting the relationship" — see
    /// <see cref="GameConditionDef"/> for the whole argument). The match is cached per worker, and a worker
    /// is already one-per-def, so the scan happens at most once per incident per session.
    ///
    /// <para/><b>What scope it registers at.</b> The civilization's, always —
    /// <c>World.World.gameConditionManager</c>. <see cref="GameConditionManager"/>'s own doc carries the
    /// reasoning: the storyteller's only target is the whole civilization, and the settlement an incident
    /// would otherwise pick usually has no interior map, so registering on a map would make the incident do
    /// nothing whenever nobody was watching. An open map still feels every one of these conditions, because
    /// its manager's parent is this one.
    ///
    /// <para/><b>Refire and duration.</b> Refire spacing needed nothing new: <c>IncidentDef.minRefireDays</c>
    /// and <c>IncidentWorker.CanFireNow</c> already enforce it against the target's <c>StoryState</c>, and
    /// the three content defs now use it. Duration did — the storyteller had no way to say how long anything
    /// lasts — and it is <see cref="GameConditionDef.durationDays"/>, rolled here through the seeded stream.
    /// <see cref="CanFireNowSub"/> additionally refuses to start a condition that is already in force, so a
    /// heat wave cannot silently stack with itself into an unsurvivable one.
    /// </summary>
    public class IncidentWorker_MakeGameCondition : IncidentWorker
    {
        private GameConditionDef? conditionDefInt;
        private bool conditionDefResolved;

        /// <summary>The condition this incident makes, or null when no content names this incident.</summary>
        public GameConditionDef? ConditionDef
        {
            get
            {
                if (!conditionDefResolved)
                {
                    conditionDefInt = ConditionFor(def);
                    conditionDefResolved = true;
                }
                return conditionDefInt;
            }
        }

        /// <summary>The scope a fired condition is registered at; null when no world exists yet, which is the
        /// one state in which a civilization-scale condition has nowhere to live.</summary>
        public static GameConditionManager? Scope => Find.World?.gameConditionManager;

        /// <summary>The single <see cref="GameConditionDef"/> naming <paramref name="incident"/>, or null.
        /// Content with two conditions claiming one incident is a content bug the conditions test catches;
        /// this takes the first so a mistake there cannot become a crash in the director.</summary>
        public static GameConditionDef? ConditionFor(IncidentDef incident)
        {
            if (incident == null) return null;
            IReadOnlyList<GameConditionDef> all = DefDatabase<GameConditionDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].incident == incident) return all[i];
            }
            return null;
        }

        protected override bool CanFireNowSub(IncidentParms parms)
        {
            GameConditionDef? conditionDef = ConditionDef;
            if (conditionDef == null) return false;
            GameConditionManager? scope = Scope;
            return scope != null && !scope.ConditionIsActive(conditionDef);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            GameConditionDef? conditionDef = ConditionDef;
            if (conditionDef == null) return false;

            GameConditionManager? scope = Scope;
            if (scope == null || scope.ConditionIsActive(conditionDef)) return false;

            GameCondition condition = GameConditionMaker.MakeConditionForDays(
                conditionDef, Rand.Range(conditionDef.durationDays));
            scope.RegisterCondition(condition);
            SendStartLetter(conditionDef);
            return true;
        }

        private static void SendStartLetter(GameConditionDef conditionDef)
        {
            LetterDef? letterDef = conditionDef.letterDef;
            if (letterDef == null) return;
            Find.LetterStack.ReceiveLetter(conditionDef.LabelCap, conditionDef.description ?? conditionDef.LabelCap, letterDef);
        }
    }
}
