using System.Globalization;

using SimWorld.Defs;
using SimWorld.Economy;
using SimWorld.Factions;
using SimWorld.Sim;
using SimWorld.World;

namespace SimWorld.Quests
{
    /// <summary>
    /// A demand for tribute, answered (RimWorld: no direct equivalent — see the translation note below).
    ///
    /// <para/><b>What was wrong.</b> <c>Quest_Tribute</c> shipped as a script that set a slate variable
    /// <c>demandSilver</c> from the threat points, sent a letter reading "A rival civilization demands
    /// tribute", waited for the offer to expire, and ended <see cref="QuestEndOutcome.Fail"/> — always.
    /// Nothing read <c>demandSilver</c>. Nothing took a coin from anybody. Nothing happened if the tribute
    /// went unpaid, so the "or threatens reprisal" in the quest's own description was a sentence with no
    /// mechanism behind it. The quest could not be answered, could not be refused, and could not do anything
    /// but fail on a timer.
    ///
    /// <para/>It is the exact mirror of the defect <see cref="QuestRewardSink"/> was written for: rewards
    /// were computed and paid into nothing. Value could not enter the civilization. It could not leave it
    /// either.
    ///
    /// <para/><b>Where the silver comes from.</b> <see cref="QuestCivilization.TreasuryOf"/> — the same seat a
    /// reward is paid into. Symmetry is the point: a reward that lands in one ledger and a tribute taken from
    /// another would be two different civilizations wearing one name.
    ///
    /// <para/><b>All or nothing.</b> A civilization that cannot cover the demand in full refuses; it does not
    /// hand over what it has. Two reasons. A tribute part-paid is not a tribute — the demanding faction either
    /// got what it asked for or it did not — and a partial payment would need a rule for what fraction buys
    /// what fraction of goodwill, which is a number nobody could source. The binary keeps the branch honest.
    ///
    /// <para/><b>Who decides.</b> Pay if the treasury can cover it. This port has no player-input model at
    /// this seam — the god layer acts through standing edicts, not per-quest prompts — so "afford it, pay it"
    /// is the civilization's standing policy rather than a choice presented and taken. An
    /// <c>EdictDef</c> that refuses tribute on principle (buying the reprisal to keep the silver) is the
    /// natural place for the choice to live once someone wants it, and is deliberately not invented here.
    ///
    /// <para/><b>Why the reprisal is applied here and not through <see cref="QuestPart_Reward"/>.</b> A
    /// negative goodwill record would have worked mechanically — <see cref="QuestRewardSink"/> already rounds
    /// and applies a negative change. But <see cref="QuestPart_Reward"/> feeds
    /// <see cref="Quest.Notify_RewardsGiven"/>, and <see cref="Quest.RewardsSummary"/> is what a quest
    /// advertises it is worth. Routing a punishment through it would put "-10 goodwill" in the payoff line. A
    /// reprisal is not a reward.
    ///
    /// <para/><b>No roll, anywhere in here.</b> Same reason the reward sink takes none: this runs inside
    /// <see cref="QuestManager.QuestManagerTick"/>, so drawing from the ambient <see cref="Rand"/> stream
    /// would shift every subsequent roll in the game by the accident of when an offer happened to expire.
    ///
    /// <para/><b>Translation, recorded.</b> RimWorld has nothing to port 1:1 here. Its equivalent demands are
    /// satisfied by physically loading goods onto a caravan and sending it to the quest giver, and this port
    /// has no caravan-to-quest-giver flow — one civilization, many settlements, and mostly no maps. So the
    /// payment is a ledger entry, the way a reward already is.
    /// </summary>
    public sealed class QuestPart_Tribute : QuestPart
    {
        /// <summary>Reason string carried on the goodwill change, for <see cref="Faction.GoodwillChanged"/>'s subscribers.</summary>
        public const string PaidReason = "TributePaid";

        /// <summary>Reason string carried on the reprisal, distinct from <see cref="PaidReason"/> so a
        /// subscriber can tell being shaken down from being punished for refusing.</summary>
        public const string RefusedReason = "TributeRefused";

        /// <summary>How much silver is demanded. Rounded away from zero; a demand that rounds to nothing is
        /// treated as no demand at all and paid for free.</summary>
        public float silverDemanded;

        /// <summary>The faction doing the demanding, by <c>defName</c> (the shape
        /// <see cref="RewardRecord.factionDefName"/> already uses, and resolved the same deterministic way —
        /// <see cref="FactionManager.FirstFactionOfDef"/>, which answers in world-generation order).</summary>
        public string? factionDefName;

        /// <summary>Goodwill applied when the tribute is paid. Zero by default: paying protection money buys
        /// the absence of a reprisal, not a friendship.</summary>
        public float goodwillOnPayment;

        /// <summary>Goodwill applied when the tribute is refused — the reprisal the quest's description
        /// promises. Negative, or the refusal costs nothing and the demand is theatre again.</summary>
        public float goodwillOnRefusal;

        public string outSignalPaid = "";

        public string outSignalRefused = "";

        /// <summary>What happened, for a test or the chronicle to read without re-deriving it. Null until the
        /// part has been enabled.</summary>
        public TributeOutcome? Outcome { get; private set; }

        public override void Enable(Signal signal)
        {
            base.Enable(signal);

            int demanded = Rounded(silverDemanded);
            Settlement? treasury = QuestCivilization.TreasuryOf(QuestCivilization.Current);
            int held = treasury != null && EconomyThingDefOf.Silver != null
                ? treasury.StoreCountOf(EconomyThingDefOf.Silver)
                : 0;

            bool paid = demanded <= held;
            int handedOver = 0;

            if (paid && demanded > 0 && treasury != null && EconomyThingDefOf.Silver != null)
            {
                treasury.AddStore(EconomyThingDefOf.Silver, -demanded);
                handedOver = demanded;
            }

            int goodwill = ApplyGoodwill(paid ? goodwillOnPayment : goodwillOnRefusal,
                paid ? PaidReason : RefusedReason);

            Outcome = new TributeOutcome(paid, demanded, handedOver, held, goodwill, treasury?.name ?? "");
            RecordChronicle(Outcome.Value);

            string outSignal = paid ? outSignalPaid : outSignalRefused;
            if (outSignal.Length > 0) quest.SendSignal(outSignal);
        }

        /// <summary>Moves the player civilization's relation with the demanding faction. Returns what actually
        /// moved, which is zero when the world has no such faction (a solo start has no rivals), when there is
        /// no player faction to hold the relation, or when the relation was already at the clamp
        /// <see cref="Faction.TryAffectGoodwillWith"/> holds it to.</summary>
        private int ApplyGoodwill(float amount, string reason)
        {
            int change = Rounded(amount);
            if (change == 0 || string.IsNullOrEmpty(factionDefName)) return 0;

            FactionManager factions = Find.FactionManager;
            Faction? player = factions.OfPlayer;
            if (player == null) return 0;

            FactionDef? def = DefDatabase<FactionDef>.GetNamedSilentFail(factionDefName!);
            if (def == null) return 0;

            Faction? other = factions.FirstFactionOfDef(def);
            if (other == null || ReferenceEquals(other, player)) return 0;

            return player.TryAffectGoodwillWith(other, change, reason: reason) ? change : 0;
        }

        /// <summary>One chronicle line, through the same <see cref="Director.Storyteller.RecordChronicle(string)"/>
        /// hook raids, births, edicts and quest rewards already use. A civilization that paid off a rival and
        /// has no record of it cannot have its own history read back to it.</summary>
        private static void RecordChronicle(TributeOutcome outcome)
        {
            if (outcome.Demanded <= 0) return;

            string line = outcome.Paid
                ? "Tribute paid: " + Num(outcome.HandedOver) + " silver out of "
                  + (outcome.Recipient.Length > 0 ? outcome.Recipient + "'s stores" : "the treasury") + "."
                : "Tribute refused: " + Num(outcome.Demanded) + " silver demanded, "
                  + Num(outcome.Held) + " held"
                  + (outcome.GoodwillApplied != 0 ? ", goodwill down " + Num(-outcome.GoodwillApplied) : "") + ".";

            Find.Storyteller.RecordChronicle(line);
        }

        private static int Rounded(float value) => (int)System.Math.Round(value, System.MidpointRounding.AwayFromZero);

        private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref silverDemanded, "silverDemanded");
            Scribe_Values.Look(ref factionDefName, "factionDefName");
            Scribe_Values.Look(ref goodwillOnPayment, "goodwillOnPayment");
            Scribe_Values.Look(ref goodwillOnRefusal, "goodwillOnRefusal");
            Scribe_Values.Look(ref outSignalPaid, "outSignalPaid", "");
            Scribe_Values.Look(ref outSignalRefused, "outSignalRefused", "");
        }
    }

    /// <summary>
    /// What one tribute demand did. Values only — no live settlement or faction a caller could reach the
    /// world through, the same shape <see cref="QuestRewardPayment"/> and <c>SettlementRaidOutcome</c> use for
    /// the same reason.
    /// </summary>
    public readonly struct TributeOutcome
    {
        internal TributeOutcome(bool paid, int demanded, int handedOver, int held, int goodwillApplied, string recipient)
        {
            Paid = paid;
            Demanded = demanded;
            HandedOver = handedOver;
            Held = held;
            GoodwillApplied = goodwillApplied;
            Recipient = recipient;
        }

        /// <summary>Whether the civilization met the demand in full. See the class doc on why there is no
        /// third answer.</summary>
        public bool Paid { get; }

        /// <summary>Silver asked for, after rounding.</summary>
        public int Demanded { get; }

        /// <summary>Silver that actually left the treasury — <see cref="Demanded"/> when paid, zero otherwise.</summary>
        public int HandedOver { get; }

        /// <summary>Silver the treasury held when the demand arrived, which is what the decision was made on.</summary>
        public int Held { get; }

        /// <summary>Goodwill points that actually moved, in whichever direction. Zero when the demanding
        /// faction does not exist in this world or the relation was already clamped.</summary>
        public int GoodwillApplied { get; }

        /// <summary>The settlement that paid, or "" when the civilization had no settlement to pay from.</summary>
        public string Recipient { get; }

        /// <summary>True when this demand left the civilization measurably different — the property the whole
        /// class exists to make true.</summary>
        public bool ChangedTheWorld => HandedOver > 0 || GoodwillApplied != 0;
    }
}
