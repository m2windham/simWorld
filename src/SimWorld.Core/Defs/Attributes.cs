using System;

namespace SimWorld.Defs
{
    /// <summary>
    /// Marks a static class whose static Def-typed fields are bound by name after loading
    /// (RimWorld: <c>RimWorld.DefOf</c>). Field name = defName unless <see cref="DefAliasAttribute"/> says otherwise.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DefOfAttribute : Attribute
    {
    }

    /// <summary>Overrides the defName a <see cref="DefOfAttribute"/> field binds to.</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class DefAliasAttribute : Attribute
    {
        public string DefName { get; }

        public DefAliasAttribute(string defName)
        {
            DefName = defName ?? throw new ArgumentNullException(nameof(defName));
        }
    }

    /// <summary>
    /// An additional XML element name that loads into this field (RimWorld: <c>Verse.LoadAlias</c>).
    /// Keeps old content files loading after a rename.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public sealed class LoadAliasAttribute : Attribute
    {
        public string Alias { get; }

        public LoadAliasAttribute(string alias)
        {
            Alias = alias ?? throw new ArgumentNullException(nameof(alias));
        }
    }

    /// <summary>
    /// Field is runtime-only: never written by the save system. <paramref name="allowLoading"/>
    /// lets XML still populate it (RimWorld: <c>Verse.Unsaved</c>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class UnsavedAttribute : Attribute
    {
        public bool AllowLoading { get; }

        public UnsavedAttribute(bool allowLoading = false)
        {
            AllowLoading = allowLoading;
        }
    }
}
