using SimWorld.Factions;

namespace SimWorld.Pawns
{
    /// <summary>
    /// The three questions every system that reports a person to the player has to ask first (RimWorld:
    /// <c>Verse.PawnUtility</c>, trimmed to them): is this one of the civilization's own, is somebody holding
    /// them, and should the narrator say anything about them at all.
    ///
    /// <para/><b>Why these live together in one place.</b> Each of them was already being asked somewhere, in
    /// a different shape: <c>Needs.Need_Mood</c>'s difficulty offset asks "player faction?" by reading
    /// <c>pawn.faction.def.isPlayer</c>, <c>AI.DoctorUtility</c> asks "held prisoner?" through
    /// <see cref="CaptureUtility.FindHostFaction"/>, and nothing asked the third at all because nothing in
    /// the core composed a letter about a pawn. Three call sites inventing the same predicate is how two of
    /// them end up disagreeing; a mental break that letters the player about a raider and a death that does
    /// not would be one bug, not two.
    ///
    /// <para/><b>Not the same question as <c>Director.StorytellerPawnEvents.IsCivilizationMember</c></b>, and
    /// the difference is deliberate. That one asks whether a pawn is on the roster the <i>threat curve</i>
    /// reads, because what it guards is a number about the civilization. These ask about a <i>person</i> —
    /// whose side are they on, who holds them — which is what a notification is about, and which is
    /// answerable without walking a roster or needing a storyteller to exist.
    /// </summary>
    public static class PawnUtility
    {
        /// <summary>One of the civilization's own: a pawn whose faction is the player's (RimWorld:
        /// <c>pawn.Faction == Faction.OfPlayer</c>). A pawn with no faction — a bare test pose, or one
        /// generated before it is placed — is not one.</summary>
        public static bool IsColonist(Pawn? pawn) => pawn?.faction != null && pawn.faction.def.isPlayer;

        /// <summary>Whoever currently holds <paramref name="pawn"/> prisoner, or null (RimWorld:
        /// <c>Pawn.HostFaction</c>; this port keeps the record on the faction, see
        /// <see cref="Pawn_GuestTracker"/>).</summary>
        public static Faction? HostFactionOf(Pawn? pawn) => pawn == null ? null : CaptureUtility.FindHostFaction(pawn);

        /// <summary>Somebody is holding this pawn prisoner (RimWorld: <c>Pawn.IsPrisoner</c>).</summary>
        public static bool IsPrisoner(Pawn? pawn) => HostFactionOf(pawn) != null;

        /// <summary>The civilization is holding this pawn prisoner (RimWorld: <c>Pawn.IsPrisonerOfColony</c>).</summary>
        public static bool IsPrisonerOfColony(Pawn? pawn)
        {
            Faction? host = HostFactionOf(pawn);
            return host != null && host.def.isPlayer;
        }

        /// <summary>
        /// Whether the narrator should raise a letter about this pawn at all (RimWorld:
        /// <c>PawnUtility.ShouldSendNotificationAbout</c>): the civilization's own people, and the prisoners
        /// it holds. A raider going berserk in someone else's town is not news, and a letter per raider is
        /// how a notification channel stops being read.
        /// </summary>
        public static bool ShouldSendNotificationAbout(Pawn? pawn) =>
            pawn != null && (IsColonist(pawn) || IsPrisonerOfColony(pawn));

        /// <summary>
        /// Substitutes a pawn's label into a content-authored message. The shipped convention is
        /// <c>{0}</c> (<c>DamageDef.deathMessage</c>, <c>MentalStateDef.beginLetter</c>), and this is a
        /// literal replace rather than <c>string.Format</c> on purpose: a stray brace in a def is a
        /// malformed letter, not a <c>FormatException</c> thrown out of the middle of a pawn dying.
        /// </summary>
        public static string FormatWithPawn(string? template, Pawn? pawn) =>
            template == null ? "" : template.Replace("{0}", pawn?.Label ?? "?");
    }
}
