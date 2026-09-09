using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Social.Ideology
{
    /// <summary>Which structural role a <see cref="MemeDef"/> plays in an ideoligion (RimWorld:
    /// <c>RimWorld.MemeCategory</c>, trimmed to the two values this port's content actually uses —
    /// RimWorld also has a third, <c>Culture</c>-flavoured category this pass does not need).</summary>
    public enum MemeCategory
    {
        /// <summary>An ideoligion's core social shape (RimWorld: e.g. "Tribalism", "Individualist"). Every
        /// <see cref="IdeoDef"/> must carry exactly one — <see cref="IdeoDef.ConfigErrors"/> enforces it,
        /// porting RimWorld's own "structure memes are mutually exclusive and mandatory" rule.</summary>
        Structure,

        /// <summary>An optional theme layered on top of the structure meme (RimWorld: most memes — "Nudism",
        /// "Bloodlust" and the like). An ideoligion can carry any number.</summary>
        Normal,
    }

    /// <summary>
    /// A broad ideoligion theme — SimWorld's port of RimWorld's <c>RimWorld.MemeDef</c>. RimWorld's own split
    /// is "memes are broad themes with slots; precepts are the specific rules" (this system's own brief); this
    /// class carries the meme half of that split. A meme contributes to an <see cref="Ideo"/> two ways:
    /// <see cref="autoPrecepts"/> (rules that come with the meme automatically, no choice involved) and
    /// <see cref="preceptSlots"/> (topics — RimWorld: <c>IssueDef</c> — the meme opens up a choice for, which
    /// an <see cref="IdeoDef"/> resolves by naming the chosen <see cref="PreceptDef"/> in its own
    /// <see cref="IdeoDef.precepts"/>).
    /// <para/>
    /// <b>Why a bare string names a slot's topic instead of a dedicated <c>IssueDef</c> Def type.</b> RimWorld
    /// has a real <c>IssueDef</c> (a topic like "Apparel" or "Cannibalism", with its own label and its own
    /// content directory of precept options). This port's own worker briefing granted content directories for
    /// <c>MemeDefs</c>, <c>PreceptDefs</c>, <c>RitualDefs</c> and <c>IdeoDefs</c> but not an <c>IssueDefs</c>
    /// one — an omission this pass takes as a deliberate simplification rather than an oversight to work
    /// around: a free-form tag (<see cref="PreceptDef.issue"/>, matched here by plain string equality) carries
    /// the same "a meme opens a topic; a precept fills it" structure with one fewer Def type and no new content
    /// directory outside this pass's granted ownership. See this module's own report for the full reasoning.
    /// </summary>
    public class MemeDef : Def
    {
        public MemeCategory category = MemeCategory.Normal;

        /// <summary>Precepts every ideoligion carrying this meme gets automatically — no slot, no choice.
        /// RimWorld: a meme's own baseline precepts (e.g. every ideoligion with the "Cannibalism" theme
        /// automatically disapproves or approves it at some fixed degree).</summary>
        public List<PreceptDef> autoPrecepts = new List<PreceptDef>();

        /// <summary>Topics (free-form tags, matched against <see cref="PreceptDef.issue"/>) this meme opens a
        /// choice for. An <see cref="IdeoDef"/> carrying this meme must fill every slot here with exactly one
        /// entry in its own <see cref="IdeoDef.precepts"/> whose <see cref="PreceptDef.issue"/> matches —
        /// <see cref="IdeoDef.ConfigErrors"/> enforces both directions (every slot filled, no orphan precept).</summary>
        public List<string> preceptSlots = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            for (int i = 0; i < preceptSlots.Count; i++)
            {
                if (string.IsNullOrEmpty(preceptSlots[i]))
                {
                    yield return "preceptSlots has an empty issue tag at index " + i + ".";
                }
            }
        }
    }
}
