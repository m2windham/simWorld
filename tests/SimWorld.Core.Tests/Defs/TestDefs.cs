using System;
using System.Collections.Generic;
using System.Xml.Linq;
using SimWorld;
using SimWorld.Defs;

namespace SimWorld.Tests.Defs
{
    public enum TestKind { None, Alpha, Beta }

    [Flags]
    public enum TestFlags { None = 0, A = 1, B = 2 }

    public class NestedData
    {
        public int a;
        public string? b;
        public List<string>? list;
        public NestedData? inner;
    }

    /// <summary>Exercises every mapping feature of the loader.</summary>
    public class TestDef : Def
    {
        public int count;
        public float weight;
        public bool flag;
        public string? text;
        public TestKind kind;
        public TestFlags flags;
        public int? optional;
        public List<string>? tags;
        public List<int>? nums;
        public IntRange range;
        public FloatRange frange;
        public SimpleCurve? curve;
        public NestedData? nested;
        public List<NestedData>? nestedList;
        public Dictionary<string, int>? dict;
        public Dictionary<string, TestDef>? defDict;
        public TestDef? other;
        public List<TestDef>? others;
        public Type? klass;

        [LoadAlias("oldCount")]
        public int renamed;

        [Unsaved]
        public int runtimeOnly;

        [Unsaved]
        public List<string> calls = new List<string>();

        [Unsaved]
        public bool otherWasResolvedInResolveReferences;

        public override void PostLoad()
        {
            calls.Add("PostLoad");
        }

        public override void ResolveReferences()
        {
            calls.Add("ResolveReferences");
            otherWasResolvedInResolveReferences = other != null;
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            calls.Add("ConfigErrors");
            if (count < 0)
            {
                yield return "count is negative";
            }
        }
    }

    public class ChildTestDef : TestDef
    {
        public string? extra;
    }

    public class ThrowingDef : Def
    {
        public override void PostLoad()
        {
            throw new InvalidOperationException("boom");
        }
    }

    public class TestCompMarker
    {
    }

    public class TestCompProperties : CompProperties
    {
        public int power;

        public TestCompProperties()
        {
            compClass = typeof(TestCompMarker);
        }
    }

    public class CustomLoaded : IXmlCustomLoad
    {
        public string? raw;

        public void LoadDataFromXmlCustom(XElement node, XmlLoadContext context)
        {
            raw = "custom:" + node.Value.Trim();
        }
    }

    public class TestDefWithCustom : Def
    {
        public CustomLoaded? custom;
        public List<CustomLoaded>? customs;
    }

    [DefOf]
    public static class TestDefOf
    {
        public static TestDef Alpha = null!;

        [DefAlias("Beta")]
        public static TestDef Second = null!;
    }

    [DefOf]
    public static class MissingDefOf
    {
        public static TestDef DoesNotExist = null!;
    }

    internal static class TestLoader
    {
        public static DefLoader Create(DefDatabase? database = null, Action<DefLoadOptions>? configure = null)
        {
            var options = new DefLoadOptions { BindDefOfs = false };
            configure?.Invoke(options);
            var types = new DefTypeResolver().AddAssembly(typeof(TestLoader).Assembly);
            return new DefLoader(database ?? new DefDatabase(), types, options);
        }

        public static DefLoadResult Load(string xml, string file = "test.xml", string pack = "Core")
        {
            return Create().AddXml(xml, file, pack).Load();
        }

        public static T Single<T>(DefLoadResult result) where T : Def
        {
            return (T)Xunit.Assert.Single(result.Defs);
        }
    }
}
