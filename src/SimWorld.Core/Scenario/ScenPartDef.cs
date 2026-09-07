using System;
using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Scenario
{
    /// <summary>Grouping for display/ordering purposes (RimWorld: <c>RimWorld.ScenPartCategory</c>).</summary>
    public enum ScenPartCategory
    {
        Fixed,
        PlayerPawnFilter,
        PlayerPawnModifier,
        StartingImportant,
        StartingItem,
        WorldThing,
        GameCondition,
        Rule,
        Misc,
    }

    /// <summary>Data half of one <see cref="ScenPart"/> kind (RimWorld: <c>RimWorld.ScenPartDef</c>).</summary>
    public class ScenPartDef : Def
    {
        public ScenPartCategory category = ScenPartCategory.Misc;

        public Type scenPartClass = null!;

        /// <summary>Higher sorts first in a scenario's <see cref="Scenario.GetFullInformationText"/>.</summary>
        public int summaryPriority;

        /// <summary>How many instances of this part one scenario may carry; -1 = unlimited. Not yet enforced
        /// anywhere (no scenario editor exists to violate it), kept for parity.</summary>
        public int maxUses = -1;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (scenPartClass == null || !typeof(ScenPart).IsAssignableFrom(scenPartClass))
            {
                yield return "scenPartClass must derive from ScenPart.";
            }
        }
    }
}
