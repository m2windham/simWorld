using System.Collections.Generic;

using SimWorld.Defs;
using SimWorld.Work;

namespace SimWorld.Crafting
{
    /// <summary>
    /// A kind of guild: the role that staffs it, the skill that measures its craftsmen, and the recipes it is
    /// allowed to queue. Content, not code — a civilization's industries are exactly the sort of thing a mod
    /// should be able to add, and the whole point of this port's Def layer is that adding one takes no C#.
    /// <para/>
    /// A guild does not carry its own bills: <see cref="Guild.Bills"/> is per settlement, because a granary
    /// full in one town says nothing about the next one. The def says what a guild of this kind <i>may</i>
    /// make; the settlement's own stack says what it is making.
    /// </summary>
    public class GuildDef : Def
    {
        /// <summary>The standing <see cref="RoleDef"/> whose holders are this guild's members. Reuses
        /// <c>work.policy</c>'s role rather than inventing a second kind of membership — a citizen is a
        /// weaver because the civilization made them one.</summary>
        public RoleDef role = null!;

        /// <summary>The skill a member's output is measured by. Separate from <see cref="RecipeDef.workSkill"/>
        /// on purpose: a guild has one trade, while its recipes may each name their own skill, and mixing the
        /// two would make a guild's output depend on which bill happened to be at the top of its stack.</summary>
        public SkillDef skill = null!;

        /// <summary>Recipes this guild may queue. A recipe not listed here cannot be given to this guild —
        /// which is what makes a guild an industry rather than a general-purpose workshop.</summary>
        public List<RecipeDef> recipes = new List<RecipeDef>();

        public bool Allows(RecipeDef recipe) => recipe != null && recipes.Contains(recipe);

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (role == null) yield return "role is null — a guild with no role has nobody who could be a member.";
            if (skill == null) yield return "skill is null — a guild's output is measured by a skill.";
            if (recipes.Count == 0) yield return "recipes is empty — a guild that may make nothing is not an industry.";

            for (int i = 0; i < recipes.Count; i++)
            {
                RecipeDef recipe = recipes[i];
                if (recipe == null)
                {
                    yield return "recipes contains a null entry.";
                    continue;
                }
                if (recipe.products == null || recipe.products.Count == 0)
                {
                    yield return recipe.defName + " has no products — a guild's whole output is its products.";
                }
                if (recipe.isSurgery)
                {
                    yield return recipe.defName + " is a surgery; a guild queue converts labour into goods, and a patient is not goods.";
                }
                if (recipe.ingredients == null) continue;
                for (int j = 0; j < recipe.ingredients.Count; j++)
                {
                    // A settlement holds a def-count ledger, not a stockpile of individual stacks, so "any
                    // meat" is a choice it has no way to make. Caught here rather than silently skipped at
                    // run time, where it would look like a guild that mysteriously never works.
                    if (Guild.FixedDefOf(recipe.ingredients[j]) == null)
                    {
                        yield return recipe.defName + " ingredient " + j
                            + " is not a fixed ingredient; a guild draws from a settlement's def-count ledger and cannot choose between candidates.";
                    }
                }
            }
        }
    }

    [DefOf]
    public static class GuildDefOf
    {
        public static GuildDef MasonsGuild = null!;
    }
}
