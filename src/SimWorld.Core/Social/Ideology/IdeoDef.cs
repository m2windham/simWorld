using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Social.Ideology
{
    /// <summary>
    /// An authored ideoligion preset (RimWorld: <c>RimWorld.IdeoDef</c> — RimWorld itself ships fixed
    /// ideoligions this shape, e.g. its base tribal/outlander presets, alongside the interactive
    /// ideoligion-creation screen this port does not build). Names the memes an ideoligion is built from and
    /// resolves every slot those memes open (<see cref="MemeDef.preceptSlots"/>) by naming the chosen
    /// <see cref="PreceptDef"/> directly in <see cref="precepts"/> — the content author making the choice a
    /// player would otherwise make interactively. <see cref="Ideo.Ideo(IdeoDef)"/> turns one of these into the
    /// runtime belief system a civilization actually holds.
    /// </summary>
    public class IdeoDef : Def
    {
        public List<MemeDef> memes = new List<MemeDef>();

        /// <summary>The specific precept chosen for each slot some <see cref="MemeDef"/> in <see cref="memes"/>
        /// opened — every entry here must be claimed by exactly one such slot (<see cref="ConfigErrors"/>
        /// enforces both directions). A meme's own <see cref="MemeDef.autoPrecepts"/> are not repeated here;
        /// see <see cref="AllPrecepts"/> for the union of both.</summary>
        public List<PreceptDef> precepts = new List<PreceptDef>();

        private List<PreceptDef>? allPreceptsCache;

        /// <summary>Every precept this ideoligion actually holds: each meme's <see cref="MemeDef.autoPrecepts"/>
        /// plus this def's own slot-filling <see cref="precepts"/>, de-duplicated. What <see cref="Ideo.Precepts"/>
        /// reads. Cached after first use, like every other Def-derived collection in this codebase
        /// (e.g. <see cref="Research.EraDef.Projects"/>).</summary>
        public IReadOnlyList<PreceptDef> AllPrecepts
        {
            get
            {
                if (allPreceptsCache == null)
                {
                    var set = new List<PreceptDef>();
                    for (int i = 0; i < memes.Count; i++)
                    {
                        List<PreceptDef> auto = memes[i].autoPrecepts;
                        for (int j = 0; j < auto.Count; j++)
                        {
                            if (!set.Contains(auto[j])) set.Add(auto[j]);
                        }
                    }
                    for (int i = 0; i < precepts.Count; i++)
                    {
                        if (!set.Contains(precepts[i])) set.Add(precepts[i]);
                    }
                    allPreceptsCache = set;
                }
                return allPreceptsCache;
            }
        }

        public override void ClearCachedData()
        {
            base.ClearCachedData();
            allPreceptsCache = null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;

            int structureCount = 0;
            for (int i = 0; i < memes.Count; i++)
            {
                if (memes[i].category == MemeCategory.Structure) structureCount++;
            }
            if (structureCount != 1)
            {
                yield return "an ideoligion must carry exactly one Structure meme (found " + structureCount + ").";
            }

            // Every slot a meme opened must be filled by something in `precepts` carrying that issue tag.
            for (int i = 0; i < memes.Count; i++)
            {
                List<string> slots = memes[i].preceptSlots;
                for (int s = 0; s < slots.Count; s++)
                {
                    bool filled = false;
                    for (int p = 0; p < precepts.Count; p++)
                    {
                        if (precepts[p].issue == slots[s]) { filled = true; break; }
                    }
                    if (!filled)
                    {
                        yield return memes[i].defName + "'s '" + slots[s] +
                            "' precept slot is not filled by any entry in precepts.";
                    }
                }
            }

            // Every chosen precept must actually belong to a slot some meme opened — no orphan precepts.
            for (int p = 0; p < precepts.Count; p++)
            {
                bool claimed = false;
                for (int i = 0; i < memes.Count && !claimed; i++)
                {
                    List<string> slots = memes[i].preceptSlots;
                    for (int s = 0; s < slots.Count; s++)
                    {
                        if (slots[s] == precepts[p].issue) { claimed = true; break; }
                    }
                }
                if (!claimed)
                {
                    yield return "precept '" + precepts[p].defName + "' (issue '" + precepts[p].issue +
                        "') is not claimed by any meme's preceptSlots.";
                }
            }
        }
    }
}
