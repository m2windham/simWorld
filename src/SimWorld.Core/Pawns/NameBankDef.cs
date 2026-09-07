using System.Collections.Generic;
using SimWorld.Defs;

namespace SimWorld.Pawns
{
    /// <summary>Which part of a <see cref="NameTriple"/> a <see cref="NameBankDef"/> supplies.</summary>
    public enum NameSlot
    {
        First,
        Nick,
        Last,
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
