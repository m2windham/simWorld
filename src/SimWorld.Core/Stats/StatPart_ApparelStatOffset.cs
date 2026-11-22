using System.Collections.Generic;
using SimWorld.Defs;
using SimWorld.Pawns;
using SimWorld.Things;

namespace SimWorld.Stats
{
    /// <summary>
    /// Adds (or with <see cref="subtract"/>, takes away) the sum of <see cref="apparelStat"/> over everything
    /// a pawn is wearing (RimWorld: <c>StatPart_ApparelStatOffset</c>). It is how a parka's
    /// <c>Insulation_Cold</c> lowers its wearer's <c>ComfyTemperatureMin</c> and a duster's
    /// <c>Insulation_Heat</c> raises <c>ComfyTemperatureMax</c>. The wiring of these two stats through this
    /// part is recalled from RimWorld's <c>Stats_Pawns_General</c>, not read.
    /// </summary>
    public sealed class StatPart_ApparelStatOffset : StatPart
    {
        public StatDef apparelStat = null!;
        public bool subtract;

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!(req.Thing is Pawn pawn) || pawn.apparel == null) return;
            IReadOnlyList<ThingWithComps> worn = pawn.apparel.WornApparel;
            float sum = 0f;
            for (int i = 0; i < worn.Count; i++)
            {
                sum += worn[i].GetStatValue(apparelStat);
            }
            val += subtract ? -sum : sum;
        }
    }
}
