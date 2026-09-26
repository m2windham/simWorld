using SimWorld.AI;
using SimWorld.Pawns;

namespace SimWorld.Stats
{
    /// <summary>
    /// Multiplies a stat while a pawn is resting (RimWorld: <c>RimWorld.StatPart_Resting</c>, 1.0 decompile).
    /// Content wires this onto <c>ImmunityGainSpeed</c> at 1.10 (wiki: <i>Immunity gain speed</i> — "Resting,
    /// sleeping, or skygazing: ×110%").
    /// <para/>
    /// <b>The rule this settles (task #104's own question).</b> RimWorld's real condition is
    /// <c>pawn.InBed() || (GetPosture() != Standing &amp;&amp; !Downed) || &lt;caravan cases&gt;</c> — three
    /// clauses OR'd together, and the middle one explicitly excludes a downed pawn. <b>A downed pawn lying on
    /// open ground, not in any bed, does <i>not</i> count as resting</b> — RimWorld's own wiki says as much in
    /// the same table: "Not resting (or downed): ×100%". Only <see cref="RestUtility.InBed"/> being true (the
    /// first, downed-independent clause) turns the bonus back on, which is why a downed patient carried into a
    /// bed gains immunity at the resting rate while the identical patient left on the ground the raid found
    /// them on does not — the bed is not "better ground", it is the only thing that grants the bonus at all
    /// while downed. This port has no <c>PawnPosture</c> or caravan system, so the middle clause narrows to
    /// <see cref="Pawn.Asleep"/> (a pawn sleeping without a bed) — the same "conscious rest without a bed"
    /// case RimWorld's clause exists for — <c>&amp;&amp; !Downed</c>, unchanged.
    /// </summary>
    public class StatPart_Resting : StatPart
    {
        public float factor = 1f;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn)) return;
            val *= RestingMultiplier(pawn);
        }

        private float RestingMultiplier(Pawn pawn) =>
            (pawn.InBed() || (pawn.Asleep && !pawn.Downed)) ? factor : 1f;
    }
}
