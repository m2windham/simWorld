using System;

using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// The one place anything in this codebase actually calls <see cref="Faction.DeclareWar"/>,
    /// <see cref="Faction.MakePeace"/> or <see cref="Faction.SignTreaty"/> — before this class, per
    /// <c>docs/design/player-first.md</c> §10, those three had zero callers anywhere in <c>src/</c>, not even
    /// from AI. Both halves of the diplomacy seam go through here instead of calling <see cref="Faction"/>
    /// directly: <c>God.View.GodCommands.Diplomacy.cs</c> for the player's own acts, <see cref="DiplomacyAI"/>
    /// for a civilization's own.
    ///
    /// <para/><b>Why this exists at all rather than each caller invoking <see cref="Faction"/> itself.</b> Two
    /// things every diplomatic act needs that <see cref="Faction"/>'s own methods deliberately do not do:
    /// <list type="bullet">
    /// <item>Stamp <see cref="FactionRelation.warStartTick"/> — <see cref="Faction"/>'s own doc says plainly it
    /// has no notion of "how long", and that is <see cref="DiplomacyAI"/>'s to answer, not <see cref="Faction"/>'s.
    /// Getting this right means every war-starting and war-ending path must call the same stamping code, which
    /// is exactly what routing all three acts through one file buys.</item>
    /// <item>Write the Chronicle line. RimWorld's own <c>Faction</c> never touches a chronicle (no such
    /// concept); a colon-prefixed free-form line here ("War declared: ", "Peace: ", "Treaty signed: ") lets
    /// <c>Director.MomentCurator</c>'s existing "first of its kind" rule pick these up with no change to that
    /// module — the first war this civilization ever fights becomes a moment for free.</item>
    /// </list>
    /// Neither belongs on <see cref="Faction"/> itself: this lane was briefed to prefer a new file over editing
    /// it, and putting bookkeeping/chronicle side effects on the same class that already has to stay a pure,
    /// engine-free relation model would also make its own doc's "does not send a message/letter yet" caveats
    /// harder to keep honest.
    /// </summary>
    public static class DiplomacyActions
    {
        /// <summary><paramref name="actor"/> declares war on <paramref name="target"/> — see
        /// <see cref="Faction.DeclareWar"/> for the refusal rules, which this never re-derives, only reuses.
        /// False (nothing changed, no chronicle line) exactly when that call refuses.</summary>
        public static bool DeclareWar(Faction actor, Faction target, string? cause = null)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (!actor.DeclareWar(target, cause)) return false;

            StampWarStart(actor, target);
            Find.Storyteller.RecordChronicle("War declared: " + actor.name + " vs " + target.name + Suffix(cause) + ".");
            return true;
        }

        /// <summary><paramref name="actor"/> makes peace with <paramref name="target"/> — see
        /// <see cref="Faction.MakePeace"/> for the refusal rules. False exactly when that call refuses.</summary>
        public static bool MakePeace(Faction actor, Faction target, string? cause = null)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (target == null) throw new ArgumentNullException(nameof(target));

            if (!actor.MakePeace(target, cause)) return false;

            ClearWarStart(actor, target);
            Find.Storyteller.RecordChronicle("Peace: " + actor.name + " and " + target.name + Suffix(cause) + ".");
            return true;
        }

        /// <summary><paramref name="actor"/> signs <paramref name="def"/> with <paramref name="target"/> — see
        /// <see cref="Faction.SignTreaty"/> for the refusal rule (a permanent-enemy pair only). When
        /// <paramref name="def"/> is a non-aggression pact signed mid-war, <see cref="Faction.SignTreaty"/>
        /// itself already calls <see cref="Faction.MakePeace"/> to end it — <see cref="warStartTick"/> is
        /// cleared here to match, the same way <see cref="MakePeace"/>'s own path clears it.</summary>
        public static bool SignTreaty(Faction actor, Faction target, TreatyDef def, string? cause = null)
        {
            if (actor == null) throw new ArgumentNullException(nameof(actor));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (def == null) throw new ArgumentNullException(nameof(def));

            bool endsAWar = def.nonAggression && actor.WarWith(target);
            if (!actor.SignTreaty(target, def, cause)) return false;

            if (endsAWar) ClearWarStart(actor, target);
            Find.Storyteller.RecordChronicle("Treaty signed: " + def.LabelCap + " (" + actor.name + " and " + target.name + ")" + Suffix(cause) + ".");
            return true;
        }

        /// <summary>How long the current war between <paramref name="a"/> and <paramref name="b"/> has run, in
        /// ticks, or null when they are not at war or the war predates this bookkeeping (see
        /// <see cref="FactionRelation.warStartTick"/>'s own doc). Read by <see cref="DiplomacyAI"/> to judge
        /// exhaustion; nothing here re-derives <see cref="Faction.WarWith"/> — a caller checks that itself.</summary>
        public static int? WarStartTick(Faction a, Faction b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            return a.RelationWith(b, allowNull: true)?.warStartTick;
        }

        private static void StampWarStart(Faction a, Faction b)
        {
            int now = Find.TickManager.TicksGame;
            FactionRelation? relA = a.RelationWith(b, allowNull: true);
            FactionRelation? relB = b.RelationWith(a, allowNull: true);
            if (relA != null) relA.warStartTick = now;
            if (relB != null) relB.warStartTick = now;
        }

        private static void ClearWarStart(Faction a, Faction b)
        {
            FactionRelation? relA = a.RelationWith(b, allowNull: true);
            FactionRelation? relB = b.RelationWith(a, allowNull: true);
            if (relA != null) relA.warStartTick = null;
            if (relB != null) relB.warStartTick = null;
        }

        private static string Suffix(string? cause) => string.IsNullOrEmpty(cause) ? "" : " — " + cause;
    }
}
