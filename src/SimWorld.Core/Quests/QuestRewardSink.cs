using System;
using System.Collections.Generic;
using System.Globalization;

using SimWorld.Defs;
using SimWorld.Director;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Quests
{
    /// <summary>
    /// What one payout of <see cref="QuestPart_Reward"/>'s records actually did. Values only — no live
    /// settlement or faction a caller could reach the world through, the same shape
    /// <see cref="SettlementRaidOutcome"/> uses for the same reason.
    /// </summary>
    public readonly struct QuestRewardPayment
    {
        internal QuestRewardPayment(
            int silverPaid, int silverUnstored, int goodwillApplied, int goodwillUndeliverable,
            int unrecognisedKinds, string recipientName)
        {
            SilverPaid = silverPaid;
            SilverUnstored = silverUnstored;
            GoodwillApplied = goodwillApplied;
            GoodwillUndeliverable = goodwillUndeliverable;
            UnrecognisedKinds = unrecognisedKinds;
            RecipientName = recipientName;
        }

        /// <summary>Silver that actually went into a settlement's stores, after
        /// <see cref="DifficultyDef.questRewardValueFactor"/>.</summary>
        public int SilverPaid { get; }

        /// <summary>Silver the quest awarded that had nowhere to land — a civilization with no settlement
        /// left. Counted rather than dropped: the whole point of this class is that a reward is never
        /// silently nothing.</summary>
        public int SilverUnstored { get; }

        /// <summary>Goodwill points actually moved, summed over every goodwill record. Zero when the relation
        /// was already at the clamp <see cref="Faction.TryAffectGoodwillWith"/> holds it to.</summary>
        public int GoodwillApplied { get; }

        /// <summary>Goodwill records whose named faction does not exist in this world (a solo start has no
        /// rivals at all) or which had no player faction to move a relation with.</summary>
        public int GoodwillUndeliverable { get; }

        /// <summary>Records whose <see cref="RewardRecord.kind"/> this sink does not know how to pay. Always
        /// zero against shipped content — see <see cref="QuestRewardSink"/>'s own doc.</summary>
        public int UnrecognisedKinds { get; }

        /// <summary>The settlement the goods went to, or "" when there was none.</summary>
        public string RecipientName { get; }

        /// <summary>True when this payout left the civilization measurably different — the property the whole
        /// class exists to make true.</summary>
        public bool ChangedTheWorld => SilverPaid > 0 || GoodwillApplied != 0;
    }

    /// <summary>
    /// Pays a quest's rewards into the civilization (the implementation of <see cref="IQuestRewardSink"/>,
    /// installed by default on every <see cref="QuestManager"/>).
    ///
    /// <para/><b>What was wrong.</b> Quests generated, ran and resolved, and
    /// <see cref="QuestPart_Reward.Enable"/> handed its <see cref="RewardRecord"/>s to an interface no type in
    /// <c>src/</c> implemented. Measured on the fixture
    /// <c>QuestRewardTests.A_completed_quest_used_to_enrich_nobody</c> builds — a fresh game, the
    /// <c>Quest_LostCaravan</c> script run to completion — the old behaviour produced: quest state
    /// <c>EndedSuccess</c>, <c>Quest.RewardsGiven</c> reading "200 silver, 5 goodwill", the seat's stores
    /// identical before and after (<c>MeleeWeapon_Knife:2</c>, no Silver entry at all), the civilization's
    /// wealth 60 before and 60 after, goodwill with the named faction 0 before and 0 after, and not one line
    /// in the chronicle. The quest said it had paid and nothing had been paid.
    ///
    /// <para/><b>Where a reward lands: the civilization's seat</b> (<see cref="CivilizationTarget.Seat"/> —
    /// the oldest settlement, ties broken by tile). Two alternatives were rejected:
    /// <list type="bullet">
    /// <item><i>The settlement the player is watching.</i> Rejected for the reason
    /// <see cref="SettlementRaidResolver"/> already gives in its own words: a civilization that only changes
    /// where the camera points is a stage set. A quest is offered to the civilization, not to a town, and the
    /// town that banks it may well be one nobody has open — which is exactly why this is paid into a
    /// <see cref="Settlement.Stores"/> ledger rather than spawned as Things on a map that mostly does not
    /// exist.</item>
    /// <item><i>A population-weighted roll, the way a raid picks its target.</i> A raid needs a place it
    /// happened; a payment needs a treasury, and inventing a roll for it costs real determinism: this runs
    /// inside <see cref="QuestManager.QuestManagerTick"/>, so drawing from the ambient <see cref="Rand"/>
    /// stream here would shift every subsequent roll in the game by the accident of when a delay elapsed.
    /// The seat needs no roll at all.</item>
    /// </list>
    /// A civilization with no settlements left has nowhere to bank goods; the silver is reported as
    /// <see cref="QuestRewardPayment.SilverUnstored"/> and said out loud in the chronicle rather than dropped.
    ///
    /// <para/><b>The two reward kinds, and why only one is scaled by difficulty.</b> Shipped content
    /// (<see cref="QuestNode_GiveReward"/>) produces exactly two: <c>silver</c> and <c>goodwill</c>.
    /// <see cref="DifficultyDef.questRewardValueFactor"/> multiplies the silver — because it is a factor on
    /// reward *value* and silver is the only value here — but it does so at <i>generation</i>, not here; see
    /// <see cref="ValueFactor"/> for why that end and not this one. Goodwill is deliberately left alone at
    /// both ends: it is a diplomatic reading clamped to [-100, 100] whose thresholds decide war and alliance
    /// (<see cref="Faction.KindFromGoodwill"/>), so scaling it by difficulty would move factions across those
    /// thresholds as a side effect of a difficulty setting, which is a different kind of change from "the pot
    /// of silver is bigger".
    ///
    /// <para/><b>A reward is never rounded away.</b> A positive award always pays at least one, the same
    /// "no engagement is ever free" floor <see cref="SettlementRaidResolver"/> applies to casualties. This is
    /// the floor's right home even though the scaling lives at generation: a low difficulty hands this sink a
    /// fractional award, and rounding it to nothing here is exactly the bug the class was written to remove.
    ///
    /// <para/>RimWorld has no equivalent to port: rewards there are self-applying <c>Reward</c> objects that
    /// drop Things onto the colony map. This port has one civilization, many settlements and mostly no maps,
    /// so the payout is a ledger entry and a relation change.
    /// </summary>
    public sealed class QuestRewardSink : IQuestRewardSink
    {
        /// <summary>The <see cref="RewardRecord.kind"/> <see cref="QuestNode_GiveReward"/> writes for a silver award.</summary>
        public const string SilverKind = "silver";

        /// <summary>The <see cref="RewardRecord.kind"/> <see cref="QuestNode_GiveReward"/> writes for a goodwill award.</summary>
        public const string GoodwillKind = "goodwill";

        /// <summary>Reason string carried on the goodwill change, for <see cref="Faction.GoodwillChanged"/>'s subscribers.</summary>
        public const string GoodwillReason = "QuestReward";

        private readonly CivilizationTarget? explicitCivilization;

        /// <summary>Resolves the civilization ambiently — the running <see cref="Game"/>'s target, or the one
        /// registered with the storyteller when a test poses a civilization without a game.</summary>
        public QuestRewardSink()
        {
        }

        /// <param name="civilization">The civilization to pay, instead of resolving one ambiently.</param>
        public QuestRewardSink(CivilizationTarget civilization)
        {
            explicitCivilization = civilization ?? throw new ArgumentNullException(nameof(civilization));
        }

        /// <summary>
        /// The civilization being paid: the one this sink was built with, else the running game's, else the
        /// one registered with the storyteller. That last fallback is not a test affordance — adaptation,
        /// threat points and refire spacing all already read "the civilization" off the storyteller's own
        /// target list, and a reward is the same question asked by a different system.
        /// </summary>
        public CivilizationTarget? Civilization => explicitCivilization ?? QuestCivilization.Current;

        /// <summary>Where goods land — see the class doc. Null when the civilization holds no settlements.
        /// The same ledger <see cref="QuestPart_Tribute"/> takes a tribute out of, through the same helper:
        /// value entering and leaving by different doors would be two civilizations wearing one name.</summary>
        public Settlement? Treasury => QuestCivilization.TreasuryOf(Civilization);

        /// <summary>
        /// Always 1. <see cref="DifficultyDef.questRewardValueFactor"/> is applied when a reward is
        /// <i>generated</i> (<c>QuestNode_GiveReward.RunInt</c>), which is where RimWorld applies it, so a
        /// record reaching this sink already carries its scaled value and scaling again would square the
        /// factor — on a 0.8 difficulty a reward would land at 0.64.
        ///
        /// <para/>Two lanes wired this field in the same batch, one at each end, and the merge caught it.
        /// Generation is the right end for a second reason beyond matching RimWorld: it is the value
        /// <c>Quest.RewardsSummary</c> reports, so what a quest advertises and what it pays are the same
        /// number. Scaling at payout would have made the quest promise one figure and the treasury receive
        /// another.
        ///
        /// <para/>Kept as a named constant rather than deleted so the decision has somewhere to live.
        /// </summary>
        public const float ValueFactor = 1f;

        /// <summary>The <see cref="IQuestRewardSink"/> entry point; see <see cref="Pay"/> for what it did.</summary>
        public void GiveRewards(IReadOnlyList<RewardRecord> rewards) => Pay(rewards);

        /// <summary>
        /// Pays every record and says what landed. Same-thread, synchronous and deterministic: no roll is
        /// taken anywhere in here (see the class doc), so the same rewards against the same world always
        /// produce the same payment.
        /// </summary>
        public QuestRewardPayment Pay(IReadOnlyList<RewardRecord> rewards)
        {
            if (rewards == null) throw new ArgumentNullException(nameof(rewards));
            if (rewards.Count == 0) return default;

            Settlement? treasury = Treasury;
            // See ValueFactor: difficulty is already applied at generation, so this is 1 by construction.
            const float factor = ValueFactor;

            int silverPaid = 0;
            int silverUnstored = 0;
            int goodwillApplied = 0;
            int goodwillUndeliverable = 0;
            int unrecognised = 0;

            for (int i = 0; i < rewards.Count; i++)
            {
                RewardRecord record = rewards[i];
                if (record == null) continue;

                switch (record.kind)
                {
                    case SilverKind:
                    {
                        int amount = Scale(record.amount, factor);
                        if (amount == 0) break;
                        if (treasury == null || EconomyThingDefOf.Silver == null)
                        {
                            silverUnstored += amount;
                            break;
                        }
                        treasury.AddStore(EconomyThingDefOf.Silver, amount);
                        silverPaid += amount;
                        break;
                    }

                    case GoodwillKind:
                    {
                        // Unscaled on purpose — see the class doc.
                        int change = Rounded(record.amount);
                        if (change == 0) break;
                        if (TryAffectGoodwill(record.factionDefName, change)) goodwillApplied += change;
                        else goodwillUndeliverable++;
                        break;
                    }

                    default:
                        unrecognised++;
                        break;
                }
            }

            var payment = new QuestRewardPayment(
                silverPaid, silverUnstored, goodwillApplied, goodwillUndeliverable, unrecognised,
                treasury?.name ?? "");
            RecordChronicle(payment);
            return payment;
        }

        /// <summary>
        /// Applies a goodwill award to the player civilization's relation with the named faction.
        ///
        /// <para/><see cref="RewardRecord.factionDefName"/> is a def name rather than a live faction (see that
        /// field's own doc), so this resolves it the one deterministic way available:
        /// <see cref="FactionManager.FirstFactionOfDef"/>, which answers in world-generation order. A world
        /// holding two instances of the same <see cref="FactionDef"/> is therefore paid to the elder of them —
        /// an ambiguity in the record's shape, not in this resolution. Returns false when the world has no
        /// such faction at all (a solo start has no rivals), when there is no player faction to hold the
        /// relation, or when the relation was already clamped and nothing moved.
        /// </summary>
        private static bool TryAffectGoodwill(string? factionDefName, int change)
        {
            if (string.IsNullOrEmpty(factionDefName)) return false;

            FactionManager factions = Find.FactionManager;
            Faction? player = factions.OfPlayer;
            if (player == null) return false;

            FactionDef? def = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName!);
            if (def == null) return false;

            Faction? other = factions.FirstFactionOfDef(def);
            if (other == null || ReferenceEquals(other, player)) return false;

            return player.TryAffectGoodwillWith(other, change, reason: GoodwillReason);
        }

        /// <summary>A reward's value after the difficulty factor, floored at one so a positive award under a
        /// positive factor is never rounded out of existence. A non-positive award scales without the floor —
        /// this is a payout, not a promise that every record is a gift.</summary>
        private static int Scale(float amount, float factor)
        {
            int scaled = Rounded(amount * factor);
            if (amount > 0f && factor > 0f && scaled < 1) scaled = 1;
            return scaled;
        }

        private static int Rounded(float value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

        /// <summary>
        /// One free-form chronicle line, through the same <see cref="Storyteller.RecordChronicle(string)"/>
        /// hook raids, births and edicts already use. A payment nobody can see is only half the fix: the
        /// other half of "a reward is real" is that the civilization's record says it arrived.
        /// </summary>
        private static void RecordChronicle(QuestRewardPayment payment)
        {
            var pieces = new List<string>();
            if (payment.SilverPaid > 0)
            {
                pieces.Add(Num(payment.SilverPaid) + " silver into " + payment.RecipientName + "'s stores");
            }
            if (payment.SilverUnstored > 0)
            {
                pieces.Add(Num(payment.SilverUnstored) + " silver with nowhere left to store it");
            }
            if (payment.GoodwillApplied != 0)
            {
                pieces.Add((payment.GoodwillApplied > 0 ? "goodwill up " : "goodwill down ")
                           + Num(Math.Abs(payment.GoodwillApplied)));
            }
            if (pieces.Count == 0) return;

            Find.Storyteller.RecordChronicle("Quest reward: " + string.Join(", ", pieces) + ".");
        }

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    }
}
