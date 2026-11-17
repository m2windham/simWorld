using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Pawns
{
    /// <summary>
    /// Tracks full names already given out this run so <see cref="PawnBioAndNameGenerator"/> never repeats one
    /// (RimWorld: <c>Verse.PawnBioAndNameGenerator</c> keeps an in-memory "used names" set too, alongside the
    /// disk-backed name banks). Process-wide by design; tests reset it with <see cref="Clear"/>.
    /// </summary>
    public static class NameUseChecker
    {
        private static readonly HashSet<string> usedFullNames = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> usedNicks = new HashSet<string>(StringComparer.Ordinal);

        public static bool NameWordIsUsed(string fullName) => usedFullNames.Contains(fullName);

        /// <summary>Whether a <see cref="NameTriple"/> already given out goes by <paramref name="nick"/> — the
        /// test RimWorld's <c>GeneratePawnName_Shuffled</c> retries its nick against
        /// (<c>NameUseChecker.AllPawnsNamesEverUsed</c>, any <c>NameTriple</c> whose <c>Nick</c> matches).</summary>
        public static bool NickIsUsed(string nick) => usedNicks.Contains(nick);

        public static void AddName(string fullName) => usedFullNames.Add(fullName);

        public static void AddName(NameTriple name)
        {
            usedFullNames.Add(name.ToStringFull);
            usedNicks.Add(name.nick);
        }

        public static void Clear()
        {
            usedFullNames.Clear();
            usedNicks.Clear();
        }
    }

    /// <summary>
    /// Generates names for freshly generated pawns (RimWorld: <c>RimWorld.PawnBioAndNameGenerator</c>, minus the
    /// scenario-linked "fixed bio" half — nothing here reads a story file). Humanlike pawns get a
    /// <see cref="NameTriple"/> drawn from gendered <see cref="NameBankDef"/> pools; everything else gets a single
    /// name (RimWorld's <c>NameSingle</c>).
    /// </summary>
    public static class PawnBioAndNameGenerator
    {
        private const int MaxTries = 50;

        /// <summary>
        /// A first + last name from the loaded banks, retried until it has not been used yet this run (falling
        /// back to a numbered surname if every combination in the banks collides), and the name the pawn goes
        /// by, which is what every readout shows (<see cref="NameTriple.ToStringShort"/>).
        /// <para/>
        /// <b>The nick is RimWorld's, retried against the nicks already in use</b>
        /// (<c>PawnBioAndNameGenerator.GeneratePawnName_Shuffled</c>, recalled from the decompiled source, which
        /// is not reachable from here to quote): up to 50 times, 15% a nickname from the nick bank (gendered one
        /// time in five), otherwise the first name or the last name evenly. It used to be the first name always,
        /// with only the full name kept unique, so a band of 20 drawn from 40 first names per gender went by
        /// three Oonas and two Ivos (the host's default game). There is no nick bank in content yet; that branch
        /// falls back to the first name. Pinned by behaviour in <c>PawnNameTests</c>, not by the constants.
        /// </para>
        /// </summary>
        public static NameTriple GeneratePawnName(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            Gender gender = pawn.gender == Gender.None ? Gender.Male : pawn.gender;

            string first = RandomNameFromBank(gender, NameSlot.First) ?? "Ash";
            string last = RandomNameFromBank(Gender.None, NameSlot.Last) ?? "Colony";
            for (int tries = 0; tries < MaxTries; tries++)
            {
                if (tries > 0)
                {
                    first = RandomNameFromBank(gender, NameSlot.First) ?? first;
                    last = RandomNameFromBank(Gender.None, NameSlot.Last) ?? last;
                }
                var candidate = new NameTriple(first, NickFor(gender, first, last), last);
                if (!NameUseChecker.NameWordIsUsed(candidate.ToStringFull))
                {
                    NameUseChecker.AddName(candidate);
                    return candidate;
                }
            }

            // Every drawn combination collided (an exhausted or tiny name bank): number the surname so
            // generation never fails outright.
            for (int suffix = 2; ; suffix++)
            {
                var candidate = new NameTriple(first, first, last + suffix.ToString(CultureInfo.InvariantCulture));
                if (!NameUseChecker.NameWordIsUsed(candidate.ToStringFull))
                {
                    NameUseChecker.AddName(candidate);
                    return candidate;
                }
            }
        }

        /// <summary>RimWorld's nick loop (see <see cref="GeneratePawnName"/>): gives up after 50 tries and keeps
        /// the last draw, as RimWorld does.</summary>
        private static string NickFor(Gender gender, string first, string last)
        {
            string nick;
            int tries = 0;
            do
            {
                tries++;
                if (Rand.Value < 0.15f)
                {
                    Gender nickGender = Rand.Value < 0.8f ? Gender.None : gender;
                    nick = RandomNameFromBank(nickGender, NameSlot.Nick) ?? first;
                }
                else
                {
                    nick = Rand.Value < 0.5f ? first : last;
                }
            }
            while (tries < 50 && NameUseChecker.NickIsUsed(nick));
            return nick;
        }

        /// <summary>Non-humanlike pawns get one name: from a name bank sharing the race's gender-less first-name
        /// pool if one is loaded, else the race's own label, numbered to stay unique.</summary>
        public static Name GenerateName(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            string baseName = RandomNameFromBank(Gender.None, NameSlot.First) ?? pawn.def.label ?? pawn.def.defName;

            string candidate = baseName;
            int suffix = 1;
            while (NameUseChecker.NameWordIsUsed(candidate))
            {
                suffix++;
                candidate = baseName + suffix.ToString(CultureInfo.InvariantCulture);
            }
            NameUseChecker.AddName(candidate);
            return new NameSingle(candidate);
        }

        /// <summary>
        /// A household surname, drawn from the same <see cref="NameSlot.Last"/> pools individual pawns use.
        /// Households are named at founding (<see cref="FamilyManager"/>); unlike pawn names these are not
        /// forced unique, because two unrelated houses sharing a name is ordinary rather than a bug.
        /// </summary>
        public static string GenerateSurname()
        {
            return RandomNameFromBank(Gender.None, NameSlot.Last) ?? "Nameless";
        }

        private static string? RandomNameFromBank(Gender gender, NameSlot slot)
        {
            List<string> pool = DefDatabase<NameBankDef>.AllDefsListForReading
                .Where(bank => bank.slot == slot && (bank.gender == gender || bank.gender == Gender.None))
                .SelectMany(bank => bank.names)
                .ToList();
            return pool.Count == 0 ? null : Rand.Element((IReadOnlyList<string>)pool);
        }
    }
}
