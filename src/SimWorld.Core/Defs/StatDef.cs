using System.Collections.Generic;
using System.Xml.Linq;

namespace SimWorld.Defs
{
    /// <summary>A named numeric quality Things and Pawns can have (RimWorld: <c>RimWorld.StatDef</c>).</summary>
    public class StatDef : Def
    {
        public float defaultBaseValue;
        public float minValue;
        public float maxValue = float.MaxValue;
        public bool hideAtValue;
        public float hideAtValueThreshold;

        /// <summary>Optional post-processing of the final value.</summary>
        public SimpleCurve? postProcessCurve;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (minValue > maxValue)
            {
                yield return "minValue " + minValue + " is greater than maxValue " + maxValue + ".";
            }
        }
    }

    /// <summary>
    /// One <c>stat = value</c> entry. Loaded with the RimWorld idiom where the element name is the
    /// StatDef and the text is the value: <c>&lt;statBases&gt;&lt;MaxHitPoints&gt;100&lt;/MaxHitPoints&gt;&lt;/statBases&gt;</c>.
    /// </summary>
    public class StatModifier : IXmlCustomLoad
    {
        public StatDef? stat;
        public float value;

        public StatModifier()
        {
        }

        public StatModifier(StatDef stat, float value)
        {
            this.stat = stat;
            this.value = value;
        }

        public void LoadDataFromXmlCustom(XElement node, XmlLoadContext context)
        {
            context.RegisterObjectWantsCrossRef(this, nameof(stat), node.Name.LocalName);
            try
            {
                value = ParseHelper.FromString<float>(node.Value);
            }
            catch (System.Exception e)
            {
                context.Error("Could not parse stat value '" + node.Value.Trim() + "' for " + node.Name.LocalName + ": " + e.Message);
            }
        }

        public override string ToString() => (stat?.defName ?? "?") + "=" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
