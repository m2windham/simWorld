using System;
using System.Collections.Generic;
using System.Linq;

using SimWorld.Sim;

using CoreSettlement = SimWorld.World.Settlement;
using CoreWorld = SimWorld.World.World;

namespace SimWorld.Factions
{
    /// <summary>
    /// The world's own half of diplomacy: a periodic, unprompted assessment that lets a civilization declare
    /// war or sue for peace with nobody at the god surface asking it to (<c>docs/design/player-first.md</c> §10:
    /// "if only the player can declare war, the world is inert"). Ticked from
    /// <see cref="FactionManager.FactionManagerTick"/> — the same "something a caller already ticks" seam
    /// <see cref="World.EmergenceManager.Tick"/> uses for its own periodic, world-scale assessment, rather than
    /// a second scheduler. Self-gated on <see cref="AssessmentIntervalTicks"/> exactly like
    /// <see cref="Faction.FactionTick"/> is, so a call on an off tick costs one modulo.
    ///
    /// <para/><b>Two independent passes, in order:</b>
    /// <list type="bullet">
    /// <item><b>War.</b> Every non-player, non-defeated civilization looks at every other civilization it is
    /// not already at war with. Bad enough goodwill (<see cref="Faction.RelationKindWith"/> already
    /// <see cref="FactionRelationKind.Hostile"/> — reused, not re-derived) and enough relative strength
    /// (<see cref="MinAttackStrengthRatio"/>, against <see cref="StrengthOf"/>) makes it a candidate; whether it
    /// actually acts <i>this</i> check is a seeded roll so a war does not fire the instant it becomes eligible
    /// every time. The player is never an actor here — <see cref="God.View.GodCommands"/> is the player's own
    /// hand — but is a perfectly ordinary target.</item>
    /// <item><b>Peace.</b> Every pair already at war, once <see cref="DiplomacyActions.WarStartTick"/> says the
    /// war has run at least <see cref="MinWarDurationForPeaceTicks"/>, lets its weaker side (by the same
    /// <see cref="StrengthOf"/> reading) roll to sue for peace. The player's own civilization never sues on its
    /// own behalf — <see cref="God.View.GodCommands.MakePeace"/> is how a human ends a war they are losing.</item>
    /// </list>
    ///
    /// <para/><b>Strength is population.</b> Nothing in this codebase yet measures a civilization's military
    /// strength directly (no aggregate combat-power stat exists at civilization scale — see
    /// <c>God.GodRollup.MeanIndustrySkill</c>'s own doc for the same gap one level down, at "industry"). Total
    /// population across a civilization's settlements is the best available stand-in and is already how
    /// <see cref="Director.SettlementRaidResolver"/> musters defenders, so this reuses that reading rather than
    /// inventing a second one. When a civilization-scale strength ledger exists this is the one call site that
    /// needs to change.
    ///
    /// <para/><b>Determinism.</b> Every roll goes through <see cref="RandomStream.ChanceSeeded"/>, hashed from
    /// the two factions' own <see cref="Faction.loadID"/> (assigned deterministically at faction creation) and
    /// the current tick (itself deterministic) — never <see cref="Rand.Current"/>, which anything else running
    /// the same tick could have already drawn from. No persisted random-stream state is needed for this: the
    /// same seed produces the same tick history produces the same loadIDs produces the same rolls, the same way
    /// <c>MapGen.GenStep.SeededStream</c> derives a stream from a seed string rather than carrying one forward.
    /// </summary>
    public static class DiplomacyAI
    {
        /// <summary>How often this assessment runs. Reuses <see cref="Faction.GoodwillCheckInterval"/> rather
        /// than inventing a second cadence for what is, in effect, the same kind of periodic per-faction
        /// check.</summary>
        public const int AssessmentIntervalTicks = Faction.GoodwillCheckInterval;

        /// <summary>
        /// How much stronger (by <see cref="StrengthOf"/>) an actor must be than its target before it will even
        /// consider declaring war — "enough relative strength" from the brief. This port's own number: RimWorld
        /// has no AI-initiated war declaration to source one from (see <see cref="Faction.WarDeclarationGoodwillChange"/>'s
        /// own doc for the same gap). Pinned by <c>DiplomacyAITests</c>' strength-gate tests rather than trusted
        /// as a literal.
        /// </summary>
        public const float MinAttackStrengthRatio = 1.5f;

        /// <summary>Mean years between an eligible civilization actually acting on a war it could declare. This
        /// port's own number, chosen so a hostile, dominant civilization moves within a few years of becoming
        /// eligible rather than the instant it does (see <see cref="World.EmergenceTuning.NewCivilizationMTBYears"/>'s
        /// own doc for the same "plausible pacing, not a literal" reasoning one system over).</summary>
        public const float DeclareWarMTBYears = 2f;

        /// <summary>How long a war must have run before its weaker side will even consider suing for peace —
        /// "worn down by a <i>long</i> war" from the brief. This port's own number.</summary>
        public const int MinWarDurationForPeaceTicks = GenDate.TicksPerYear;

        /// <summary>Mean years between an eligible weaker side actually suing for peace, once
        /// <see cref="MinWarDurationForPeaceTicks"/> has passed. This port's own number.</summary>
        public const float PeaceMTBYears = 1f;

        /// <summary>
        /// Call once per game tick (from <see cref="FactionManager.FactionManagerTick"/>). Only acts every
        /// <see cref="AssessmentIntervalTicks"/> ticks, and only once a <see cref="World.World"/> exists to
        /// measure strength against — before a game exists (or in a bare-<see cref="FactionManager"/> test that
        /// never wires one) there is nothing for a civilization to have gone to war over.
        /// </summary>
        public static void Tick(FactionManager manager)
        {
            if (manager == null) throw new ArgumentNullException(nameof(manager));
            if (Find.TickManager.TicksGame % AssessmentIntervalTicks != 0) return;

            CoreWorld? world = Find.World;
            if (world == null) return;

            List<Faction> factions = manager.GetFactions().ToList();
            if (factions.Count < 2) return;

            AssessWarDecisions(factions, world);
            AssessPeaceOffers(factions, world);
        }

        // ---- war: a hostile, dominant civilization may turn on a weaker one ----

        private static void AssessWarDecisions(List<Faction> factions, CoreWorld world)
        {
            for (int i = 0; i < factions.Count; i++)
            {
                Faction actor = factions[i];
                // The player's own hand is God.View.GodCommands.DeclareWar; this pass is every OTHER
                // civilization deciding for itself, which is the whole point of §10's "the world's half".
                if (actor.def.isPlayer) continue;

                for (int j = 0; j < factions.Count; j++)
                {
                    if (i == j) continue;
                    AssessWarDecision(actor, factions[j], world);
                }
            }
        }

        private static void AssessWarDecision(Faction actor, Faction target, CoreWorld world)
        {
            if (actor.WarWith(target)) return;
            if (actor.RelationKindWith(target) != FactionRelationKind.Hostile) return; // "bad enough goodwill" — Faction's own threshold, not re-derived.
            if (actor.HasNonAggressionPactWith(target)) return; // DeclareWar would refuse anyway; skip the roll entirely.

            float actorStrength = StrengthOf(actor, world);
            float targetStrength = StrengthOf(target, world);
            if (targetStrength <= 0f) return; // nothing yet to be measurably stronger than.
            if (actorStrength < targetStrength * MinAttackStrengthRatio) return; // not "enough relative strength".

            if (!RollOccurs(actor, target, "DeclareWar", DeclareWarMTBYears)) return;

            DiplomacyActions.DeclareWar(actor, target, actor.name + " judged " + target.name + " weak enough to attack");
        }

        // ---- peace: the weaker side of a long war may sue for it ----

        private static void AssessPeaceOffers(List<Faction> factions, CoreWorld world)
        {
            // Each unordered pair once: WarWith is symmetric, and a war has exactly one "weaker side" to ask.
            for (int i = 0; i < factions.Count; i++)
            {
                for (int j = i + 1; j < factions.Count; j++)
                {
                    AssessPeaceOffer(factions[i], factions[j], world);
                }
            }
        }

        private static void AssessPeaceOffer(Faction a, Faction b, CoreWorld world)
        {
            if (!a.WarWith(b)) return;

            int? startTick = DiplomacyActions.WarStartTick(a, b);
            if (startTick == null) return; // predates this bookkeeping (e.g. a permanent-enemy pair) — never assessed.
            if (Find.TickManager.TicksGame - startTick.Value < MinWarDurationForPeaceTicks) return; // not yet "a long war".

            float strengthA = StrengthOf(a, world);
            float strengthB = StrengthOf(b, world);
            Faction weaker;
            Faction stronger;
            if (strengthA < strengthB) { weaker = a; stronger = b; }
            else if (strengthB < strengthA) { weaker = b; stronger = a; }
            else return; // evenly matched: neither side is the one "worn down" relative to the other.

            // The player sues for peace through God.View.GodCommands.MakePeace, never on their own initiative —
            // the mirror of AssessWarDecisions excluding the player as an actor.
            if (weaker.def.isPlayer) return;
            if (weaker.def.permanentEnemy || stronger.def.permanentEnemy) return; // MakePeace would refuse anyway.

            if (!RollOccurs(weaker, stronger, "SueForPeace", PeaceMTBYears)) return;

            DiplomacyActions.MakePeace(weaker, stronger, weaker.name + " sued for peace after a long war");
        }

        // ---- shared: strength reading and the deterministic roll ----

        /// <summary>Total population across every settlement <paramref name="faction"/> holds — see the class
        /// doc's note on why population stands in for military strength here.</summary>
        private static int StrengthOf(Faction faction, CoreWorld world)
        {
            int total = 0;
            foreach (CoreSettlement settlement in world.Settlements)
            {
                if (ReferenceEquals(settlement.faction, faction)) total += settlement.TotalPopulation;
            }
            return total;
        }

        /// <summary>
        /// A deterministic, unseeded-stream roll for one (actor, other, kind-of-decision) tuple at the current
        /// tick — see the class doc's determinism note. <paramref name="label"/> keeps the war-declaration roll
        /// and the peace-suing roll independent for the same pair, even on the same check.
        /// </summary>
        private static bool RollOccurs(Faction actor, Faction other, string label, float mtbYears)
        {
            int pairSeed = SimWorld.World.GenText.StableStringHash(actor.loadID + "|" + label + "|" + other.loadID);
            int seed = MurmurHash.Combine(pairSeed, Find.TickManager.TicksGame);
            float probability = RandomStream.MTBEventProbability(mtbYears, GenDate.TicksPerYear, AssessmentIntervalTicks);
            return RandomStream.ChanceSeeded(probability, seed);
        }
    }
}
