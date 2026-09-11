using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using SimWorld.Defs;

namespace SimWorld.Tests.Wiring
{
    /// <summary>One seam that nothing reaches, as the audit found it.</summary>
    public sealed class Seam
    {
        public Seam(string check, string id, string detail)
        {
            Check = check;
            Id = id;
            Detail = detail;
        }

        /// <summary>Which check produced it — the first word of a baseline line.</summary>
        public string Check { get; }

        /// <summary>Stable identity, e.g. <c>SimWorld.Pawns.Pawn_TierTracker.Notify_AttentionChanged</c>.</summary>
        public string Id { get; }

        /// <summary>What is missing, in words. Not part of the baseline key, so it may be reworded freely.</summary>
        public string Detail { get; }

        /// <summary>The baseline key: check and identity, whitespace-separated.</summary>
        public string Key => Check + " " + Id;

        public override string ToString() => Key + "  — " + Detail;
    }

    /// <summary>
    /// Finds seams in <c>src/SimWorld.Core</c> that nothing reaches.
    ///
    /// <para/><b>Why this exists.</b> Over three batches nearly every real defect in this project had one
    /// shape: a module that was complete, tested, and called by nothing.
    /// <c>Pawn_TierTracker.Notify_AttentionChanged</c> had no caller, so every citizen stayed Full forever.
    /// The whole <c>Combat/</c> module was unreachable from play — no attack JobGiver existed, so a raid
    /// arrived and everyone kept farming. <c>IEnvironmentSampler</c> was implemented only by a test fake, so
    /// the Beauty need sat at its base level. Each one passed its own tests perfectly, because <i>a module
    /// tested in isolation cannot tell that nothing reaches it</i>. Every one was found by hand, at real
    /// cost. This finds them mechanically.
    ///
    /// <para/><b>Three sources of truth</b>, each used where it is strongest: reflection over the compiled
    /// assembly for what exists, <see cref="SourceIndex"/> over <c>src/</c> for what is reached, and
    /// <see cref="ContentSurvey"/> over the loaded Defs for what the shipped content actually says. No check
    /// decides anything from a name alone.
    ///
    /// <para/><b>The rule that keeps the checks honest.</b> Evidence is weighed by which answer raises a
    /// seam. Where finding a use <i>creates</i> an entry — "code reads this field, and no content sets it" —
    /// the search is strict, because a mistaken sighting invents a defect. Where finding a use <i>silences</i>
    /// one — "content sets this field, and nothing reads it" — the search is as generous as it can be, because
    /// a missed sighting invents a defect. The same question ("is this read?") is therefore asked two
    /// different ways depending on which way a wrong answer would hurt.
    ///
    /// <para/><b>Two checks that were built, measured and dropped.</b> Both were tried against the real
    /// core and both failed the only test that matters for a check like this — could a reader believe every
    /// entry it produced?
    /// <list type="bullet">
    /// <item><description><i>Any public method with no caller in src/.</i> 177 hits. Most are service APIs a
    /// system offers — <c>Faction.DeclareWar</c>, <c>ThingFilter.SetAllowAll</c> — and telling those from a
    /// module nothing reaches needs judgement on each one, which is not a thing a baseline can carry
    /// honestly. <c>Notify_*</c> survives as the decidable special case: the convention says the method is an
    /// inbound event, so an uncalled one is a system that never hears.</description></item>
    /// <item><description><i>Any field no Def sets, whatever its default.</i> 106 hits against 51 for the
    /// strict form. The extra 55 are knobs with a deliberate initialiser that no content overrides, where the
    /// only honest reason is "the default is the intended value" — 55 lines of that and nobody reads the
    /// baseline again.</description></item>
    /// </list>
    ///
    /// <para/><b>On noise.</b> A test that fails on every legitimately-unused API gets deleted, and then the
    /// defect class comes back. So each check is written to under-report where it must guess, and what
    /// remains is pinned by a reviewed baseline (<c>dormant-seams.txt</c>) in which every entry carries the
    /// reason it is dormant. The audit is green today and fails the moment a <i>new</i> seam goes dark.
    /// </summary>
    public static class WiringAudit
    {
        public const string CheckInterface = "interface";
        public const string CheckNotify = "notify";
        public const string CheckWorkerClass = "worker";
        public const string CheckContentSilent = "content-silent";
        public const string CheckCodeDeaf = "code-deaf";
        public const string CheckUnwritten = "unwritten";

        /// <summary>Worker base classes that exist to stand in for a system nobody has built yet.</summary>
        private static readonly string[] PlaceholderMarkers = { "Placeholder", "_Pending", "Stub", "NoOp", "Unimplemented" };

        /// <summary>
        /// Fields every Def carries. They are filled by the loader itself rather than by any one system, so
        /// reporting them would say nothing about wiring.
        /// </summary>
        private static readonly HashSet<string> DefBaseFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "defName", "label", "description", "index", "fileName", "packName", "ignoreConfigErrors",
        };

        /// <summary>
        /// Methods whose caller is the runtime, a base class or a framework rather than a line of <c>src/</c>,
        /// so "nothing calls it" is not evidence of anything.
        /// </summary>
        private static readonly HashSet<string> FrameworkMethods = new HashSet<string>(StringComparer.Ordinal)
        {
            "ExposeData", "PostLoad", "ResolveReferences", "ConfigErrors", "ClearCachedData", "Dispose",
            "ToString", "Equals", "GetHashCode", "CompareTo", "GetEnumerator", "Deconstruct",
        };

        // ---------------------------------------------------------------------------------------------
        // 1. An interface nothing in src/ implements — or implements but never builds.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Interfaces declared in the core with no live implementation behind them.
        ///
        /// <para/>This is the Beauty case exactly: <c>IEnvironmentSampler</c> existed, <c>Need_Beauty</c> read
        /// through it, and the only class implementing it lived in the test project. The need therefore sat
        /// at its base level in every real game, while its tests — which supplied their own fake — passed.
        ///
        /// <para/>Two degrees are reported, and the second is the one that hides: an interface with no
        /// implementing type in the core assembly at all, and one whose implementations are all unreachable —
        /// never constructed in <c>src/</c>, never handed to <c>typeof</c>, never named by content as a
        /// worker class, never built by the content loader. An implementation only a test ever builds is,
        /// from the game's point of view, not an implementation.
        /// </summary>
        public static IReadOnlyList<Seam> InterfacesWithoutImplementation(
            IReadOnlyList<Type> types, SourceIndex source, ContentSurvey content)
        {
            var seams = new List<Seam>();
            Type[] concrete = types.Where(t => !t.IsInterface && !t.IsAbstract).ToArray();

            foreach (Type candidate in types.Where(t => t.IsInterface))
            {
                Type[] implementations = concrete.Where(t => candidate.IsAssignableFrom(t)).ToArray();
                if (implementations.Length == 0)
                {
                    seams.Add(new Seam(CheckInterface, Name(candidate),
                        "no type in SimWorld.Core implements it; any implementation lives in the tests or the host"));
                    continue;
                }

                if (implementations.Any(t => IsReachable(t, source, content))) continue;

                seams.Add(new Seam(CheckInterface, Name(candidate),
                    "implemented by " + string.Join(", ", implementations.Select(t => SimpleName(t)))
                    + ", and src/ never builds any of them"));
            }

            return seams;
        }

        /// <summary>
        /// Whether anything in <c>src/</c> can ever hold an instance of this type: the content loader builds
        /// it, shipped content names it as a worker class, or the code constructs it — directly, or
        /// reflectively from a <c>typeof</c>.
        /// </summary>
        private static bool IsReachable(Type type, SourceIndex source, ContentSurvey content)
        {
            if (typeof(Def).IsAssignableFrom(type)) return true;
            if (content.IsContentLoaded(type)) return true;
            if (content.WorkerTypeNames.Contains(type.FullName ?? type.Name)) return true;

            string site;
            return source.IsConstructed(SimpleName(type), out site) || source.IsNamedInTypeof(SimpleName(type), out site);
        }

        // ---------------------------------------------------------------------------------------------
        // 2. A Notify_* hook nothing raises.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// <c>Notify_*</c> methods with no call site in <c>src/</c>.
        ///
        /// <para/>The naming convention makes this the sharpest check in the audit: a <c>Notify_</c> method is
        /// by construction something another system is supposed to raise, so an uncalled one is never "an API
        /// no caller has needed yet" — it is a system that will never hear the event.
        /// <c>Pawn_TierTracker.Notify_AttentionChanged</c> and
        /// <c>StoryWatcher_Adaptation.Notify_ColonistDied</c> were both exactly this, and both silently
        /// bounded nothing.
        ///
        /// <para/>Abstract and interface declarations are skipped: they are raised, if at all, through an
        /// implementation, and each implementation is checked on its own.
        /// </summary>
        public static IReadOnlyList<Seam> NotifyHooksWithoutCaller(IReadOnlyList<Type> types, SourceIndex source)
        {
            var seams = new List<Seam>();
            foreach (Type type in types)
            {
                if (type.IsInterface) continue;
                foreach (MethodInfo method in DeclaredMethods(type))
                {
                    if (!method.Name.StartsWith("Notify_", StringComparison.Ordinal)) continue;
                    if (method.IsAbstract) continue;

                    string site;
                    if (source.HasCallSite(method.Name, out site)) continue;
                    seams.Add(new Seam(CheckNotify, Name(type) + "." + method.Name,
                        "declared but never raised: no call site anywhere in src/"));
                }
            }
            return seams;
        }

        // ---------------------------------------------------------------------------------------------
        // 3. A Def naming a worker that does nothing.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Defs whose worker/driver/class field resolves to a placeholder, to an abstract type, or to nothing.
        ///
        /// <para/>Content-side dormancy is the worst kind to find by hand, because the Def looks finished: the
        /// HeatWave, ColdSnap and Flashstorm incidents ship with a label, a chance and a category, and all
        /// point at <c>IncidentWorker_Placeholder</c>, which returns true and does nothing. Eighteen work
        /// types once shipped with <c>giverClass</c> left at its <c>WorkGiver_Pending</c> default: they
        /// appeared in every pawn's work priorities and could never produce a job.
        ///
        /// <para/>A null is only reported for a field the code declares non-nullable. A <c>Type?</c> worker
        /// field is an override — <c>ThoughtDef.thoughtClass</c> falls back to memory or situational when
        /// unset — and reporting fifty defs for declining an override would be pure noise.
        ///
        /// <para/>Reported per worker type rather than per Def: six incidents pointing at one placeholder are
        /// one dormant seam and one baseline line, not six.
        /// </summary>
        public static IReadOnlyList<Seam> PlaceholderWorkerClasses(IEnumerable<Def> defs)
        {
            var byWorker = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (Def def in defs)
            {
                foreach (FieldInfo field in ContentFields(def.GetType()))
                {
                    if (field.FieldType != typeof(Type)) continue;
                    if (!field.Name.EndsWith("Class", StringComparison.Ordinal)) continue;

                    var value = (Type?)field.GetValue(def);
                    string key;
                    string reason;
                    if (value == null)
                    {
                        if (IsDeclaredNullable(field)) continue;
                        key = Name(field.DeclaringType!) + "." + field.Name + " = null";
                        reason = "declared non-nullable and left null, so the def names no worker at all";
                    }
                    else if (IsPlaceholder(value))
                    {
                        key = Name(field.DeclaringType!) + "." + field.Name + " = " + Name(value);
                        reason = "a stand-in worker: it validates, returns, and implements nothing";
                    }
                    else if (value.IsAbstract)
                    {
                        key = Name(field.DeclaringType!) + "." + field.Name + " = " + Name(value);
                        reason = "abstract, so the def can never produce a worker";
                    }
                    else
                    {
                        continue;
                    }

                    SortedSet<string>? users;
                    if (!byWorker.TryGetValue(key, out users))
                    {
                        users = new SortedSet<string>(StringComparer.Ordinal);
                        byWorker.Add(key, users);
                        reasons[key] = reason;
                    }
                    users.Add(def.defName);
                }
            }

            return byWorker.Select(pair => new Seam(CheckWorkerClass, pair.Key,
                reasons[pair.Key] + "; " + Summarise(pair.Value))).ToList();
        }

        private static bool IsPlaceholder(Type type) =>
            PlaceholderMarkers.Any(marker => type.Name.IndexOf(marker, StringComparison.Ordinal) >= 0);

        // ---------------------------------------------------------------------------------------------
        // 4. Content and code talking past each other.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Fields the code reads where every shipped Def leaves the value null, false, zero or empty.
        ///
        /// <para/>This is the <c>ThingDef.flammability</c> shape: the system is wired end to end, the content
        /// is silent, and the branch behind the field can never be taken. It looks like a finished feature
        /// from either side alone — the code has a reader, the XML has a schema — and only the two together
        /// show that nothing happens.
        ///
        /// <para/>Restricted to fields whose value is the <i>type's</i> default everywhere, not merely
        /// unchanged from the class's own initialiser. A field initialised to <c>1f</c> that no Def overrides
        /// is a knob nobody turns, and the simulation still does the thing; reporting those added fifty-five
        /// entries whose only honest reason was "the default is the intended value", which is how a baseline
        /// turns into wallpaper.
        /// </summary>
        public static IReadOnlyList<Seam> ContentFieldsNoDefSets(ContentSurvey content, SourceIndex source, IReadOnlyList<Type> types)
        {
            var scopes = new Dictionary<Type, IReadOnlyCollection<string>>();
            var seams = new List<Seam>();
            foreach (FieldUsage usage in Ordered(content.Fields))
            {
                if (usage.SetByContent || usage.EverNonDefault) continue;

                string site;
                if (!source.IsRead(usage.Field.Name, ScopeNames(usage.Field.DeclaringType!, types, scopes), out site)) continue;

                seams.Add(new Seam(CheckContentSilent, usage.Id,
                    "read at " + site + ", and all " + usage.Instances.ToString(CultureInfo.InvariantCulture)
                    + " loaded object(s) leave it at null/false/zero/empty"));
            }
            return seams;
        }

        /// <summary>
        /// Fields shipped content sets that no code ever reads.
        ///
        /// <para/>The mirror image, and the one that fools a reviewer hardest: <c>Tribesperson.weaponTags</c>
        /// named the weapons a tribal should spawn holding, and nothing anywhere read the field. The content
        /// is right, the schema is right, and the pawns spawn empty-handed. A content author has no way to
        /// see this from the XML, and a reader of the code has no reason to look.
        /// </summary>
        public static IReadOnlyList<Seam> ContentFieldsNoCodeReads(ContentSurvey content, SourceIndex source)
        {
            var seams = new List<Seam>();
            foreach (FieldUsage usage in Ordered(content.Fields))
            {
                if (!usage.SetByContent) continue;

                string site;

                // Unscoped on purpose. Here a *missing* read is what raises the seam, so the search has to be
                // as generous as possible: any read anywhere, even one that turns out to be a same-named field
                // in another system, is enough to keep quiet. Demanding the read sit in a file that names the
                // declaring type added fifty-odd false reports — fields like NeedDef.seekerFallPerHour, read
                // through a base class's def property in a file that never writes the word "NeedDef".
                if (source.IsRead(usage.Field.Name, out site)) continue;

                seams.Add(new Seam(CheckCodeDeaf, usage.Id,
                    "shipped content sets it and no line of src/ ever reads it"));
            }
            return seams;
        }

        private static IEnumerable<FieldUsage> Ordered(IEnumerable<FieldUsage> usages) =>
            usages.OrderBy(u => u.Id, StringComparer.Ordinal);

        // ---------------------------------------------------------------------------------------------
        // 5. State with a reader and no writer.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Public simulation state that <c>src/</c> reads and never writes.
        ///
        /// <para/><c>AreaManager.Home</c> existed and nothing ever populated it; <c>CivilizationTarget.Map</c>
        /// was read and set by nothing. A field like that answers every query with its initial value, so the
        /// system on top of it runs, stays self-consistent, and means nothing.
        ///
        /// <para/>Writes are detected broadly — assignment, compound assignment, increment, and <c>ref</c>/
        /// <c>out</c> arguments, which is how <c>Scribe_Values.Look</c> writes a field on load — so the check
        /// errs towards calling state live. Anything the content loader fills in is excluded: its writer is
        /// the XML, and <see cref="ContentFieldsNoDefSets"/> asks the content-side version of this question.
        /// </summary>
        public static IReadOnlyList<Seam> StateWithNoWriter(IReadOnlyList<Type> types, SourceIndex source, ContentSurvey content)
        {
            var scopes = new Dictionary<Type, IReadOnlyCollection<string>>();
            var seams = new List<Seam>();
            foreach (Type type in types)
            {
                if (type.IsInterface || typeof(Def).IsAssignableFrom(type)) continue;
                if (content.IsContentLoaded(type)) continue;

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral) continue;

                    string readSite;
                    if (!source.IsRead(field.Name, ScopeNames(type, types, scopes), out readSite)) continue;

                    string writeSite;
                    if (source.IsWritten(field.Name, out writeSite)) continue;

                    seams.Add(new Seam(CheckUnwritten, Name(type) + "." + field.Name,
                        "read at " + readSite + " and assigned nowhere in src/"));
                }
            }
            return seams;
        }

        // ---------------------------------------------------------------------------------------------
        // Running the lot.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Every check, over one assembly's types, one source tree and one loaded content set.</summary>
        public static IReadOnlyList<Seam> Run(IReadOnlyList<Type> types, SourceIndex source, IReadOnlyList<Def> defs)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (defs == null) throw new ArgumentNullException(nameof(defs));

            ContentSurvey content = ContentSurvey.Of(defs);
            var seams = new List<Seam>();
            seams.AddRange(InterfacesWithoutImplementation(types, source, content));
            seams.AddRange(NotifyHooksWithoutCaller(types, source));
            seams.AddRange(PlaceholderWorkerClasses(defs));
            seams.AddRange(ContentFieldsNoDefSets(content, source, types));
            seams.AddRange(ContentFieldsNoCodeReads(content, source));
            seams.AddRange(StateWithNoWriter(types, source, content));
            return seams.OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        }

        // ---------------------------------------------------------------------------------------------
        // Shared plumbing.
        // ---------------------------------------------------------------------------------------------

        /// <summary>Public instance fields a content file can populate, walking up the hierarchy.</summary>
        public static IEnumerable<FieldInfo> ContentFields(Type type)
        {
            for (Type? t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.IsLiteral) continue;
                    if (t == typeof(Def) && DefBaseFields.Contains(field.Name)) continue;
                    yield return field;
                }
            }
        }

        /// <summary>
        /// Whether the field is declared as a nullable reference — <c>Type?</c> rather than <c>Type</c>.
        /// Read from the compiler's own nullability metadata, so the check believes the declaration rather
        /// than guessing from the value it happens to find.
        /// </summary>
        public static bool IsDeclaredNullable(FieldInfo field)
        {
            const string Nullable = "System.Runtime.CompilerServices.NullableAttribute";
            const string NullableContext = "System.Runtime.CompilerServices.NullableContextAttribute";

            foreach (CustomAttributeData attribute in field.CustomAttributes)
            {
                if (attribute.AttributeType.FullName != Nullable || attribute.ConstructorArguments.Count == 0) continue;
                object? value = attribute.ConstructorArguments[0].Value;
                if (value is byte flag) return flag == 2;
                if (value is IReadOnlyList<CustomAttributeTypedArgument> flags && flags.Count > 0)
                {
                    return flags[0].Value is byte first && first == 2;
                }
            }

            for (Type? t = field.DeclaringType; t != null; t = t.DeclaringType)
            {
                foreach (CustomAttributeData attribute in t.CustomAttributes)
                {
                    if (attribute.AttributeType.FullName != NullableContext || attribute.ConstructorArguments.Count == 0) continue;
                    if (attribute.ConstructorArguments[0].Value is byte context) return context == 2;
                }
            }
            return false;
        }

        /// <summary>
        /// The names a file must mention for a read of this type's field to count: the type itself and every
        /// type that inherits the field from it.
        /// </summary>
        private static IReadOnlyCollection<string> ScopeNames(
            Type declaring, IReadOnlyList<Type> types, Dictionary<Type, IReadOnlyCollection<string>> cache)
        {
            IReadOnlyCollection<string>? names;
            if (cache.TryGetValue(declaring, out names)) return names;

            var set = new HashSet<string>(StringComparer.Ordinal) { SimpleName(declaring) };
            foreach (Type type in types)
            {
                if (declaring.IsAssignableFrom(type)) set.Add(SimpleName(type));
            }
            cache.Add(declaring, set);
            return set;
        }

        private static IEnumerable<MethodInfo> DeclaredMethods(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => !m.IsSpecialName && !FrameworkMethods.Contains(m.Name));

        private static string Summarise(SortedSet<string> defNames)
        {
            const int Shown = 4;
            string listed = string.Join(", ", defNames.Take(Shown));
            if (defNames.Count > Shown)
            {
                listed += " and " + (defNames.Count - Shown).ToString(CultureInfo.InvariantCulture) + " more";
            }
            return defNames.Count.ToString(CultureInfo.InvariantCulture) + " def(s): " + listed;
        }

        private static string Name(Type type) => type.FullName ?? type.Name;

        /// <summary>The type's name as it is written in source: <c>DefRegistry</c>, not <c>DefRegistry`1</c>.</summary>
        private static string SimpleName(Type type)
        {
            string name = type.Name;
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name.Substring(0, tick);
        }
    }
}
