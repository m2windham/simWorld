using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// A Def-driven agreement two civilizations can sign (SimWorld's own translation — RimWorld has no
    /// treaty system at all, only goodwill and a handful of incident-driven swings; see docs/status.json's
    /// <c>economy</c> system <c>translation</c> field). Terms are flags a treaty carries rather than
    /// separate treaty <em>types</em>, so content can combine them (a single "alliance" TreatyDef with both
    /// set) without this module inventing a type per combination — the same "flags, not a type per
    /// combination" shape <c>FactionDef</c> already uses for <c>permanentEnemy</c>/<c>hostileToFactionlessHumanlikes</c>/etc.
    /// </summary>
    public class TreatyDef : Def
    {
        /// <summary>
        /// While an instance of this treaty is active between two factions, <see cref="Faction.DeclareWar"/>
        /// between them refuses (the entry condition a pact exists to enforce), and signing this treaty
        /// while already at war ends the war outright (see <see cref="Faction.SignTreaty"/>) — the exit
        /// condition, a peace treaty by another name.
        /// </summary>
        public bool nonAggression;

        /// <summary>
        /// While an instance of this treaty is active between two factions, trading between them gets
        /// <see cref="tradeAccessPriceGain"/> folded into <c>TradeDeal.settlementGain</c> — see
        /// <see cref="Faction.TradeAccessPriceGainWith"/> and <c>Economy.SettlementTradeUtility.OpenSession</c>.
        /// </summary>
        public bool tradeAccess;

        /// <summary>
        /// Price gain applied to trade between the two signatories while a <see cref="tradeAccess"/> treaty
        /// is active — the same units and role as <c>TradeDeal.settlementGain</c>/<c>negotiatorGain</c> (a
        /// fraction, combined and clamped to [0, 1] before <c>TradeUtility</c> applies it). This port's own
        /// number: no RimWorld source exists for it.
        /// </summary>
        public float tradeAccessPriceGain;

        /// <summary>Days this treaty stays active once signed; &lt;= 0 means it never expires on its own (see <see cref="Treaty.IsActive"/>).</summary>
        public int durationDays;

        /// <summary>One-off goodwill change applied to both signatories the moment the treaty is signed (see <see cref="Faction.SignTreaty"/>). This port's own number.</summary>
        public int signingGoodwill;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (!nonAggression && !tradeAccess)
            {
                yield return "TreatyDef '" + defName + "' sets neither nonAggression nor tradeAccess — it would do nothing.";
            }
        }
    }

    /// <summary>
    /// One signed instance of a <see cref="TreatyDef"/> between two factions, recorded on each side's own
    /// <see cref="FactionRelation"/> (<see cref="FactionRelation.treaties"/>) exactly the way goodwill
    /// already is — a diplomatic fact about the relation, not about either faction alone.
    /// </summary>
    public sealed class Treaty : IExposable
    {
        public TreatyDef def = null!;

        /// <summary><see cref="Sim.TickManager.TicksGame"/> at signing.</summary>
        public int startTick;

        /// <summary>For Scribe's deep-load construction.</summary>
        public Treaty()
        {
        }

        public Treaty(TreatyDef def, int startTick)
        {
            this.def = def;
            this.startTick = startTick;
        }

        /// <summary>Absolute tick this treaty lapses at; meaningless (never reached) when <see cref="TreatyDef.durationDays"/> &lt;= 0 — check <see cref="IsActive"/>, not this, for expiry.</summary>
        public int ExpiresAtTick => startTick + def.durationDays * GenDate.TicksPerDay;

        /// <summary>Still in force at <paramref name="atTick"/>: permanent (durationDays &lt;= 0) or not yet past <see cref="ExpiresAtTick"/>.</summary>
        public bool IsActive(int atTick) => def.durationDays <= 0 || atTick < ExpiresAtTick;

        public void ExposeData()
        {
            TreatyDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref startTick, "startTick", 0);
        }

        public override string ToString() => def?.defName + "@" + startTick;
    }

    /// <summary>
    /// Explicit war/peace, kept separate from the Hostile/Neutral/Ally goodwill-derived relation kind
    /// (SimWorld's own translation — RimWorld has nothing like it: goodwill crossing -75 alone marks a
    /// faction Hostile and eligible to raid, with no further "are we actually at war" question. A
    /// civilization-scale game asks that question explicitly instead — <see cref="Faction.DeclareWar"/> and
    /// <see cref="Faction.MakePeace"/> are its own entry/exit acts, not a goodwill threshold silently
    /// crossed — see docs/status.json's <c>economy</c> system <c>translation</c> field). A pair can be
    /// diplomatically Hostile (raids happen) without being at War, and a pair at War is always also Hostile
    /// (<see cref="Faction.DeclareWar"/> forces goodwill down when it declares).
    /// </summary>
    public enum WarState
    {
        Peace,
        War,
    }
}
