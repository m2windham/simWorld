using System;
using System.Collections.Generic;
using System.Reflection;

namespace SimWorld.Defs
{
    /// <summary>
    /// Marks a static class that mints <b>implied Defs</b> — Defs built by code rather than authored in XML
    /// (RimWorld: the <c>ThingDefGenerator_*</c> family, called from <c>Verse.DefGenerator</c>). The class must
    /// expose <c>public static int EnsureGenerated(DefDatabase database)</c>, adding only what is missing and
    /// returning how many Defs it added, so that running it twice is the second run adding nothing.
    /// <para/>
    /// Found by attribute scan rather than by a hard-coded list, exactly as <see cref="DefOfAttribute"/>
    /// classes are: a generator lives with the system it belongs to (<c>Things.CorpseDefGenerator</c> is
    /// layers above <c>Defs/</c> and this file may not name it), and a new one needs no edit here.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ImpliedDefsAttribute : Attribute
    {
    }

    /// <summary>
    /// Runs every <see cref="ImpliedDefsAttribute"/> generator as the last step of a load
    /// (RimWorld: <c>Verse.DefGenerator.GenerateImpliedDefs_PostResolve</c>).
    ///
    /// <para/><b>Why this has to happen inside the load and not on first use.</b> A Def set that grows during
    /// play is a mutable global, and two things in this codebase take a <i>snapshot</i> of it and keep that
    /// snapshot for ever: <c>Crafting.ThingFilter</c> flattens its allowed set on first use
    /// (<c>SetAllowAll(null)</c> copies <see cref="DefDatabase{T}.AllDefsListForReading"/> as it stands that
    /// instant), and <c>Crafting.ThingCategoryDef</c> caches its child Defs the first time anybody asks.
    /// Whichever of "the snapshot" and "the minting" happens first therefore decides the answer forever —
    /// and <see cref="DefDatabase.Global"/> outlives a game, so in a process that has already run one game
    /// the minting has happened and in a fresh one it has not. <b>Two runs of one seed then diverge with no
    /// difference in any observable state</b>: the measured case was a settlement's granary, painted before
    /// the first animal died, which allowed every corpse def in the second run of a process and none in the
    /// first — so one run hauled its dead and the other did not, hours of simulated time before anything
    /// visibly differed. Minting here makes the Def set a pure function of the content, which is what every
    /// snapshot downstream already assumes it is.
    ///
    /// <para/>Generators run in ordinal order of their type's full name, so the order Defs are added in — and
    /// therefore <see cref="DefDatabase{T}.AllDefsListForReading"/>'s own order — is the same on every load.
    /// </summary>
    public static class ImpliedDefHelper
    {
        /// <summary>The method an <see cref="ImpliedDefsAttribute"/> class must expose.</summary>
        public const string GeneratorMethodName = "EnsureGenerated";

        /// <summary>Runs every generator against <paramref name="database"/>; returns how many Defs were added.</summary>
        public static int GenerateAll(DefDatabase database, DefTypeResolver types, List<DefLoadError> errors)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (types == null) throw new ArgumentNullException(nameof(types));
            if (errors == null) throw new ArgumentNullException(nameof(errors));

            var generators = new List<Type>();
            foreach (Type type in types.AllTypesWithAttribute<ImpliedDefsAttribute>())
            {
                generators.Add(type);
            }
            generators.Sort(static (a, b) => string.CompareOrdinal(a.FullName ?? a.Name, b.FullName ?? b.Name));

            int added = 0;
            for (int i = 0; i < generators.Count; i++)
            {
                Type type = generators[i];
                MethodInfo? method = type.GetMethod(
                    GeneratorMethodName,
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(DefDatabase) },
                    null);
                if (method == null || method.ReturnType != typeof(int))
                {
                    errors.Add(new DefLoadError(
                        "ImpliedDefs class " + type.Name + " has no 'public static int "
                        + GeneratorMethodName + "(DefDatabase)'."));
                    continue;
                }

                try
                {
                    added += (int)(method.Invoke(null, new object[] { database }) ?? 0);
                }
                catch (TargetInvocationException e)
                {
                    errors.Add(new DefLoadError(
                        "ImpliedDefs generator " + type.Name + " threw: " + (e.InnerException ?? e).Message));
                }
            }
            return added;
        }
    }
}
