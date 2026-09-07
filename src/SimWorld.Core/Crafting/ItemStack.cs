using System;
using System.Xml.Linq;
using SimWorld.Defs;

namespace SimWorld.Crafting
{
    /// <summary>
    /// A quantity of one ThingDef, standing in for a real Thing until the Thing/Map module lands. Ingredient
    /// candidates handed to <see cref="BillIngredientsFinder"/> and products returned by
    /// <see cref="GenRecipe.MakeRecipeProducts"/> are expressed as these.
    /// </summary>
    public record ItemStack(ThingDef Def, int Count, ThingDef? Stuff = null, QualityCategory? Quality = null, float HitPointsPercent = 1f)
    {
        /// <summary>Set by <see cref="GenRecipe.PostProcessProduct"/> when a cooking roll comes out bad.</summary>
        public bool Poisoned { get; init; }
    }

    /// <summary>
    /// A fixed ThingDef+count entry (RimWorld: <c>RimWorld.ThingDefCountClass</c>). XML shape mirrors
    /// <see cref="StatModifier"/>: the element name names the ThingDef, the text is the count
    /// (<c>&lt;WoodLog&gt;20&lt;/WoodLog&gt;</c>).
    /// </summary>
    public class ThingDefCountClass : IXmlCustomLoad
    {
        public ThingDef thingDef = null!;
        public int count;

        public ThingDefCountClass()
        {
        }

        public ThingDefCountClass(ThingDef thingDef, int count)
        {
            this.thingDef = thingDef ?? throw new ArgumentNullException(nameof(thingDef));
            this.count = count;
        }

        public void LoadDataFromXmlCustom(XElement node, XmlLoadContext context)
        {
            context.RegisterObjectWantsCrossRef(this, nameof(thingDef), node.Name.LocalName);
            try
            {
                count = ParseHelper.FromString<int>(node.Value);
            }
            catch (Exception e)
            {
                context.Error("Could not parse count '" + node.Value.Trim() + "' for " + node.Name.LocalName + ": " + e.Message);
            }
        }

        public override string ToString() => count + "x " + (thingDef?.defName ?? "?");
    }
}
