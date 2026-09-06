using System;
using System.Collections.Generic;

namespace SimWorld.Sim
{
    /// <summary>
    /// Implemented by anything that saves itself (RimWorld: <c>Verse.IExposable</c>). <see cref="ExposeData"/>
    /// runs once when saving and three times when loading — LoadingVars, ResolvingCrossRefs, PostLoadInit —
    /// with the same <c>Scribe_*.Look</c> calls each time; each mode acts on the calls it cares about.
    /// </summary>
    public interface IExposable
    {
        void ExposeData();
    }

    /// <summary>Anything other objects may reference by id across a save (RimWorld: <c>Verse.ILoadReferenceable</c>).</summary>
    public interface ILoadReferenceable
    {
        string GetUniqueLoadID();
    }

    /// <summary>Phase of the save/load pass (RimWorld: <c>Verse.LoadSaveMode</c>).</summary>
    public enum LoadSaveMode
    {
        Inactive,
        Saving,
        /// <summary>Values, Defs and deep objects are read; references are only recorded.</summary>
        LoadingVars,
        /// <summary>Every recorded reference is resolved against the loaded objects.</summary>
        ResolvingCrossRefs,
        /// <summary>Everything is wired; objects rebuild derived state.</summary>
        PostLoadInit,
    }

    /// <summary>How a collection element is stored (RimWorld: <c>Verse.LookMode</c>).</summary>
    public enum LookMode
    {
        /// <summary>Infer: parsable → Value, Def → Def, IExposable → Deep. References must be explicit.</summary>
        Undefined,
        Value,
        Def,
        Deep,
        Reference,
    }

    public sealed class ScribeException : Exception
    {
        public ScribeException(string message) : base(message)
        {
        }
    }

    /// <summary>Identity comparer for keying by object, ignoring overridden Equals.</summary>
    internal sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
