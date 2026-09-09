using System;
using System.Collections.Generic;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Pawns;
using SimWorld.Pawns.Generation;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// What a generated pawn group is for (RimWorld: <c>Verse.PawnGroupKindDef</c>) — Combat, Peaceful,
    /// Trader. <see cref="PawnGroupMakerUtility"/> and <see cref="Director.IncidentWorker_RaidEnemy"/> only
    /// ever ask for <see cref="PawnGroupKindDefOf.Combat"/> so far; the others exist in content so a future
    /// caravan/trader system can name itself consistently with RimWorld's own defs.
    /// </summary>
    public class PawnGroupKindDef : Def
    {
    }

    /// <summary>
    /// One entry in a <see cref="PawnGroupMaker"/>'s roster (RimWorld: <c>RimWorld.PawnGenOption</c>): a pawn
    /// kind and how strongly it's favoured relative to the others once it's affordable.
    /// </summary>
    public sealed class PawnGenOption
    {
        public PawnKindDef kind = null!;

        public float selectionWeight = 1f;

        /// <summary>Points this option spends when chosen (RimWorld: <c>PawnGenOption.Cost</c> reads
        /// <c>kind.combatPower</c> directly — a pawn kind's generation recipe and its price against the raid
        /// budget are the same number).</summary>
        public float Cost => kind.combatPower;

        public IEnumerable<string> ConfigErrors()
        {
            if (kind == null) { yield return "PawnGenOption has no kind."; yield break; }
            if (selectionWeight <= 0f) yield return "PawnGenOption '" + kind.defName + "' selectionWeight must be positive.";
            if (kind.combatPower <= 0f) yield return "PawnGenOption '" + kind.defName + "' kind has no positive combatPower to spend against.";
        }
    }

    /// <summary>
    /// Inputs to squad generation (RimWorld: <c>RimWorld.PawnGroupMakerParms</c>), trimmed to what a Combat
    /// group needs: who is fielding it, how many points to spend, and — for the caller's own bookkeeping —
    /// which tactic picked this squad.
    /// </summary>
    public sealed class PawnGroupMakerParms
    {
        public Faction faction = null!;
        public PawnGroupKindDef groupKind = null!;
        public float points;
        public RaidStrategyDef? raidStrategy;
    }

    /// <summary>
    /// One way a faction can field a pawn group (RimWorld: <c>RimWorld.PawnGroupMaker</c>): a
    /// <see cref="PawnGroupKindDef"/> plus the weighted <see cref="PawnGenOption"/>s
    /// <see cref="GeneratePawns"/> spends <see cref="PawnGroupMakerParms.points"/> against.
    /// </summary>
    public sealed class PawnGroupMaker
    {
        public PawnGroupKindDef kindDef = null!;

        /// <summary>Relative weight this maker is picked among others of the same <see cref="kindDef"/> on the same faction (RimWorld: <c>PawnGroupMaker.commonality</c>).</summary>
        public float commonality = 1f;

        public List<PawnGenOption>? options;

        public bool CanGenerateFrom(PawnGroupMakerParms parms) =>
            parms != null && ReferenceEquals(kindDef, parms.groupKind) && options != null && options.Count > 0;

        /// <summary>
        /// Spends <paramref name="parms"/>.points against <see cref="options"/>
        /// (<see cref="PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints"/>, RimWorld's
        /// <c>PawnGroupMaker.GeneratePawns</c> for a Combat group) and generates one full pawn per chosen
        /// option through <see cref="PawnGenerator.GeneratePawn"/> — a raider is a real pawn with traits,
        /// skills and a name, armed by <see cref="PawnWeaponGenerator"/> through the ordinary generation
        /// pipeline, not a stat block.
        /// </summary>
        public List<Pawn> GeneratePawns(PawnGroupMakerParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));
            var result = new List<Pawn>();
            if (options == null || options.Count == 0) return result;

            List<PawnGenOption> chosen = PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints(parms.points, options, Rand.Current);
            foreach (PawnGenOption option in chosen)
            {
                var request = new PawnGenerationRequest(option.kind, mustBeCapableOfViolence: true, faction: parms.faction);
                result.Add(PawnGenerator.GeneratePawn(request));
            }
            return result;
        }

        public IEnumerable<string> ConfigErrors()
        {
            if (kindDef == null) yield return "PawnGroupMaker has no kindDef.";
            if (options != null)
            {
                foreach (PawnGenOption option in options)
                {
                    foreach (string error in option.ConfigErrors()) yield return error;
                }
            }
        }
    }

    /// <summary>
    /// Entry points other systems call instead of reaching into a specific faction's <see cref="PawnGroupMaker"/>
    /// list themselves (RimWorld: <c>RimWorld.PawnGroupMakerUtility</c>).
    /// </summary>
    public static class PawnGroupMakerUtility
    {
        /// <summary>The maker this faction's def has for <paramref name="parms"/>.groupKind spends the points; empty when it has none.</summary>
        public static List<Pawn> GeneratePawns(PawnGroupMakerParms parms)
        {
            if (parms == null) throw new ArgumentNullException(nameof(parms));
            if (parms.faction == null) throw new ArgumentException("PawnGroupMakerParms has no faction.", nameof(parms));
            PawnGroupMaker? maker = parms.faction.def.GetGroupMaker(parms.groupKind);
            return maker == null ? new List<Pawn>() : maker.GeneratePawns(parms);
        }

        /// <summary>RimWorld's own safety cap on the spend loop below — it never picks more options than this, no matter how cheap they are.</summary>
        public const int MaxIterations = 1000;

        /// <summary>
        /// Weighted-random spend of <paramref name="pointsTotal"/> against <paramref name="options"/>
        /// (RimWorld: <c>PawnGroupMakerUtility.ChoosePawnGenOptionsByPoints</c>): repeatedly picks one
        /// currently-affordable option by <see cref="PawnGenOption.selectionWeight"/> and deducts its
        /// <see cref="PawnGenOption.Cost"/>, until nothing left fits the remaining budget.
        /// <para/>
        /// Never returns empty when <paramref name="options"/> is non-empty and <paramref name="pointsTotal"/>
        /// is positive — RimWorld's raids are never literally zero-pawn, so a budget too small for even the
        /// cheapest option still buys that one pawn (the squad running slightly over budget rather than not
        /// existing at all).
        /// </summary>
        public static List<PawnGenOption> ChoosePawnGenOptionsByPoints(float pointsTotal, List<PawnGenOption> options, RandomStream rand)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (rand == null) throw new ArgumentNullException(nameof(rand));

            var chosen = new List<PawnGenOption>();
            float pointsLeft = pointsTotal;
            for (int i = 0; i < MaxIterations; i++)
            {
                List<PawnGenOption> affordable = options.Where(o => o.Cost <= pointsLeft).ToList();
                if (affordable.Count == 0) break;
                if (!GenCollection.TryRandomElementByWeight((IReadOnlyList<PawnGenOption>)affordable, o => o.selectionWeight, rand, out PawnGenOption picked)) break;
                chosen.Add(picked);
                pointsLeft -= picked.Cost;
            }

            if (chosen.Count == 0 && pointsTotal > 0f && options.Count > 0)
            {
                PawnGenOption cheapest = options.OrderBy(o => o.Cost).First();
                chosen.Add(cheapest);
            }
            return chosen;
        }
    }
}
