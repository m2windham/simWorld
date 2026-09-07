using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// A pawn's name (RimWorld: <c>Verse.Name</c>). <see cref="NameTriple"/> for humanlike pawns;
    /// <see cref="NameSingle"/> for everything else (animals, and anything else with just one name).
    /// Saved polymorphically via <c>Scribe_Deep</c> with a <c>Class</c> attribute.
    /// </summary>
    public abstract class Name : IExposable
    {
        /// <summary>Every part, RimWorld's "First 'Nick' Last" (or the single name, for <see cref="NameSingle"/>).</summary>
        public abstract string ToStringFull { get; }

        /// <summary>The short form used in most UI (RimWorld: nickname, or the single name).</summary>
        public abstract string ToStringShort { get; }

        /// <summary>False for a name with nothing in it (a freshly-constructed instance before it is filled in).</summary>
        public abstract bool IsValid { get; }

        public abstract void ExposeData();

        public override string ToString() => ToStringFull;
    }

    /// <summary>First/nick/last name (RimWorld: <c>Verse.NameTriple</c>).</summary>
    public class NameTriple : Name
    {
        public string first = "";
        public string nick = "";
        public string last = "";

        public NameTriple()
        {
        }

        public NameTriple(string first, string nick, string last)
        {
            this.first = first ?? "";
            this.nick = nick ?? "";
            this.last = last ?? "";
        }

        /// <summary>"First Last" when the nick is just the first or last name (the common case); otherwise
        /// "First 'Nick' Last" (RimWorld's readout when a pawn actually goes by something else).</summary>
        public override string ToStringFull
        {
            get
            {
                if (last.Length == 0 && nick.Length == 0) return first;
                if (nick.Length == 0 || nick == first || nick == last)
                {
                    return last.Length == 0 ? first : (first.Length == 0 ? last : first + " " + last);
                }
                return first + " '" + nick + "' " + last;
            }
        }

        public override string ToStringShort => nick.Length > 0 ? nick : first;

        public override bool IsValid => first.Length > 0 || nick.Length > 0 || last.Length > 0;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref first, "first", "");
            Scribe_Values.Look(ref nick, "nick", "");
            Scribe_Values.Look(ref last, "last", "");
        }
    }

    /// <summary>A single name with no parts (RimWorld: <c>Verse.NameSingle</c>) — most animals, and anything
    /// else that only ever needs one word.</summary>
    public class NameSingle : Name
    {
        public string name = "";

        public NameSingle()
        {
        }

        public NameSingle(string name)
        {
            this.name = name ?? "";
        }

        public override string ToStringFull => name;
        public override string ToStringShort => name;
        public override bool IsValid => name.Length > 0;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "");
        }
    }
}
