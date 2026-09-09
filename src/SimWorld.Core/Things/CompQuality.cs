using SimWorld.Crafting;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>Data half of <see cref="CompQuality"/> (RimWorld: <c>RimWorld.CompProperties_Quality</c>). No
    /// tunables of its own — RimWorld's real version has none either; the def opts a Thing into carrying a
    /// quality tier just by listing this comp.</summary>
    public class CompProperties_Quality : CompProperties
    {
        public CompProperties_Quality()
        {
            compClass = typeof(CompQuality);
        }
    }

    /// <summary>
    /// A spawned Thing's craftsmanship tier (RimWorld: <c>RimWorld.CompQuality</c>). Before this,
    /// <see cref="Crafting.QualityCategory"/> existed only on <see cref="ItemStack"/> — a crafting-time record
    /// with nothing to carry it once a Thing exists. This is that missing carrier: <see cref="Stats.StatPart_Quality"/>
    /// reads it off a live Thing the same way RimWorld's stat pipeline does.
    /// </summary>
    public class CompQuality : ThingComp
    {
        private QualityCategory qualityInt = QualityCategory.Normal;

        public QualityCategory Quality => qualityInt;

        /// <summary>Sets the tier directly — a recipe's roll, a trader's stock, or map generation (RimWorld:
        /// <c>CompQuality.SetQuality</c>). No "improved by" source is tracked: nothing in this port re-rolls
        /// quality after creation (RimWorld's smithing-for-quality minigame is out of scope here).</summary>
        public void SetQuality(QualityCategory q) => qualityInt = q;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref qualityInt, "quality", QualityCategory.Normal);
        }

        public override string? CompInspectStringExtra() => qualityInt.ToString();
    }
}
