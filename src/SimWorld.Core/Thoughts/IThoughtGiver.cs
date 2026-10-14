namespace SimWorld.Thoughts
{
    /// <summary>
    /// A Thing that hands a thought to whoever looks at it (RimWorld: <c>RimWorld.IThoughtGiver</c>).
    /// <see cref="PawnObserver"/> asks every Thing it can see; the answer is a memory to gain, or null. The
    /// first implementer is <see cref="Things.Corpse"/>: a body that is lying about is a thing to have seen.
    /// </summary>
    public interface IThoughtGiver
    {
        Thought_Memory? GiveObservedThought();
    }
}
