namespace SimWorld.Pawns
{
    public partial class PawnKindDef
    {
        /// <summary>
        /// The kind's label for more than one (RimWorld: <c>PawnKindDef.labelPlural</c>, read through
        /// <c>GenLabel.BestKindLabel(kind, gender, plural: true)</c>). Content sets it where English is
        /// irregular ("woolly oxen"); otherwise the label takes the regular ending (<see cref="Defs.LabelPlural"/>).
        /// The fourth playtest's letter read "a pack of 4 woolly ox".
        /// </summary>
        public string? labelPlural;

        /// <summary>The label for <paramref name="count"/> of the kind: singular for one, plural otherwise.</summary>
        public string LabelFor(int count) => Defs.LabelPlural.For(label ?? defName, count, labelPlural);
    }
}
