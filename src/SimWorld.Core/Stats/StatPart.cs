namespace SimWorld.Stats
{
    /// <summary>
    /// A pluggable step in a stat's <see cref="StatWorker.FinalizeValue"/> pass (RimWorld: <c>RimWorld.StatPart</c>).
    /// No concrete part ships with this module yet: RimWorld's own capacity/trait/hediff modifiers are core to
    /// <see cref="StatWorker.GetValueUnfinalized"/> rather than StatParts (see that method's remarks), and the
    /// two RimWorld StatParts that are true post-processing steps — quality and stuff-derived — need a live
    /// <c>CompQuality</c>/apparel-stuff on a Thing that this codebase does not have yet (Crafting's
    /// <c>QualityCategory</c> exists only on <c>ItemStack</c>, not on a spawned Thing). The hook stays real and
    /// tested (see <c>StatWorkerTests</c>) so the next module that needs a part only has to write the subclass.
    /// </summary>
    public abstract class StatPart
    {
        public abstract void TransformValue(StatRequest req, ref float val);
    }
}
