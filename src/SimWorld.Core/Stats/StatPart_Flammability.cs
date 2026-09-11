using SimWorld.Defs;

namespace SimWorld.Stats
{
    /// <summary>
    /// Zeroes <c>Flammability</c> for the kinds of Thing that, in RimWorld, carry an explicit
    /// <c>&lt;Flammability&gt;0&lt;/Flammability&gt;</c> in their own <c>statBases</c>.
    /// <para/>
    /// <b>Translation, and why.</b> RimWorld's Flammability stat defaults to 1 and every non-flammable Def
    /// says so itself, one <c>statBases</c> line at a time — hundreds of them across its content. This port's
    /// content is a fraction of that size but is edited by several lanes at once, and the failure mode of a
    /// lost merge on a shared content file is silent (CLAUDE.md: "one lane's addition simply is not there any
    /// more") — here that would mean a mountain quietly becoming flammable. So the two rules that hold for a
    /// whole <i>category</i> of Def, rather than per-Def taste, are stated once here instead:
    /// <list type="bullet">
    /// <item><description><see cref="ThingDef.mineable"/> — natural rock. This codebase already treats
    /// <c>mineable</c> as its standing line between natural rock and something somebody built
    /// (<c>ListerThings.BuildingArtificial</c>, <c>WorkGiver_Repair.IsRepairable</c>,
    /// <c>WorkGiver_Miner</c>), and it lands in the same place RimWorld's per-Def zero does.</description></item>
    /// <item><description><see cref="ThingCategory.Ethereal"/> and <see cref="ThingCategory.Mote"/> — things
    /// with no physical substance, <see cref="Things.Fire"/> itself first among them. Without this a fire
    /// would count as fuel for itself and for its neighbours.</description></item>
    /// </list>
    /// Per-Def flammability that is <i>not</i> categorical still goes in content the RimWorld way: a
    /// <c>statBases</c> entry on the Def, or a <c>stuffProps.statFactors</c> entry on the material it is built
    /// from (steel and stone blocks carry the zero factor, which is what makes a stone wall not burn while a
    /// wooden one does).
    /// </summary>
    public sealed class StatPart_Flammability : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            ThingDef? def = req.Def;
            if (def == null) return;
            if (def.mineable
                || def.category == ThingCategory.Ethereal
                || def.category == ThingCategory.Mote)
            {
                val = 0f;
            }
        }
    }
}
