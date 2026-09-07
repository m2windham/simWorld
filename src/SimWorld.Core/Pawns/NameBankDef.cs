using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Which part of a <see cref="NameTriple"/> a <see cref="NameBankDef"/> supplies. <see cref="RegionPrefix"/>
    /// and <see cref="RegionSuffix"/> are not pawn name parts at all — <c>World.RegionNameMaker</c> reuses the
    /// same bank mechanism to name world regions (spec §5b.1) rather than inventing a second, code-side naming
    /// scheme; <see cref="NameBankDef.gender"/> is ignored for both.
    /// </summary>
    public enum NameSlot
    {
        First,
        Nick,
        Last,
        RegionPrefix,
        RegionSuffix,
    }

    /// <summary>
    /// A pool of invented names for one gender/slot (RimWorld ships these as flat text files under Names/;
    /// ported here as ordinary content so <see cref="PawnBioAndNameGenerator"/> can draw from them). Surnames
    /// are conventionally loaded with <see cref="Gender.None"/> so both genders share one pool.
    /// </summary>
    public class NameBankDef : Def
    {
        public Gender gender = Gender.None;
        public NameSlot slot;
        public List<string> names = new List<string>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (names.Count == 0) yield return "name bank has no names.";
        }
    }
}
