using System.Collections.Generic;
using System.Globalization;
using SimWorld.Defs;
using SimWorld.Work;

namespace SimWorld.Pawns
{
    /// <summary>Which half of a pawn's biography a <see cref="BackstoryDef"/> fills (RimWorld: <c>RimWorld.BackstorySlot</c>).</summary>
    public enum BackstorySlot
    {
        Childhood,
        Adulthood,
    }

    /// <summary>One skill bump a backstory or trait degree grants.</summary>
    public class SkillGain
    {
        public SkillDef? skill;
        public int amount;

        public override string ToString() => (skill?.defName ?? "?") + "+" + amount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A trait a backstory or <see cref="PawnKindDef"/> forces onto every pawn that gets it.</summary>
    public class BackstoryTrait
    {
        public TraitDef? def;
        public int degree;
    }

    /// <summary>
    /// A slice of a pawn's biography (RimWorld: <c>RimWorld.BackstoryDef</c>, ported from RimWorld's separate
    /// hand-written backstory database into ordinary Defs). <see cref="Generation.PawnGenerator"/> picks one for
    /// <see cref="BackstorySlot.Childhood"/> and, for pawns old enough, one for <see cref="BackstorySlot.Adulthood"/>;
    /// both grant skill points, may force or forbid traits, and can bar whole categories of work outright.
    /// </summary>
    public class BackstoryDef : Def
    {
        public string? title;
        public string? titleShort;
        public string? baseDesc;
        public BackstorySlot slot;
        public List<SkillGain>? skillGains;
        public WorkTags workDisables = WorkTags.None;

        /// <summary>Work tags the pawn must still be able to do for this backstory to be eligible (e.g. a
        /// hunter backstory requiring <see cref="WorkTags.Violent"/>).</summary>
        public WorkTags requiredWorkTags = WorkTags.None;

        public List<BackstoryTrait>? forcedTraits;
        public List<TraitDef>? disallowedTraits;

        /// <summary>Pools <see cref="PawnKindDef.backstoryCategories"/> draws from ("Civil", "Tribal", …).</summary>
        public List<string>? spawnCategories;

        /// <summary>False keeps this out of random generation entirely (a scenario-only or unique backstory).</summary>
        public bool shuffleable = true;

        public bool InSpawnCategory(string category) => spawnCategories != null && spawnCategories.Contains(category);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (string.IsNullOrEmpty(title)) yield return "backstory has no title.";
            foreach (string error in BackstoryTraitValidation.Errors(forcedTraits, disallowedTraits)) yield return error;
        }
    }
}
