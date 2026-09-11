using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// A Thing that can ride on another Thing instead of merely sharing its cell (RimWorld:
    /// <c>Verse.AttachableThing</c>). <see cref="Fire"/> is the only one this pass ships: a fire on a cell has
    /// no <see cref="parent"/> and stays put, a fire on a pawn has one and travels with them.
    /// <para/>
    /// <b>Translation — no <c>CompAttachBase</c>.</b> RimWorld keeps the back-pointer on the parent, in a
    /// <c>CompAttachBase</c> that every attachable Def has to declare, and <c>Thing.HasAttachment</c> reads
    /// that list. Declaring it here would mean editing every race and building Def that can ever catch
    /// fire — shared content files, several lanes deep (CLAUDE.md). The link is one-way instead: the
    /// attachment knows its parent, and <see cref="FireUtility.GetAttachedFire"/> finds it by looking in the
    /// parent's own cell, which is exactly where <see cref="Tick"/> keeps it. At most one fire ever occupies
    /// a cell (<see cref="FireUtility.TryStartFireIn"/> enforces it), so that search is O(things in one cell).
    /// </summary>
    public abstract class AttachableThing : Thing
    {
        /// <summary>The Thing this rides on, or null for a free-standing one sitting on a cell.</summary>
        public Thing? parent;

        /// <summary>Extra line this attachment contributes to its parent's inspect pane (RimWorld:
        /// <c>AttachableThing.InspectStringAddon</c>).</summary>
        public abstract string InspectStringAddon { get; }

        /// <summary>Binds this attachment to <paramref name="newParent"/>; the caller spawns it afterwards
        /// (RimWorld: <c>AttachableThing.AttachTo</c>).</summary>
        public virtual void AttachTo(Thing newParent)
        {
            parent = newParent;
        }

        /// <summary>
        /// Keeps an attached Thing on its parent's cell and kills it with its parent. Free-standing
        /// attachments (no <see cref="parent"/>) pay nothing beyond the null check.
        /// </summary>
        public override void Tick()
        {
            base.Tick();
            if (parent == null) return;
            if (parent.Destroyed || !parent.Spawned)
            {
                Destroy();
                return;
            }
            if (parent.Position != Position) Position = parent.Position;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Thing? p = parent;
            Scribe_References.Look(ref p, "attachParent");
            parent = p;
        }
    }
}
