using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using SimWorld.Defs;
using SimWorld.Sim;
using SimWorld.Tests.Defs;
using Xunit;

namespace SimWorld.Tests.Sim
{
    public class Item : IExposable, ILoadReferenceable
    {
        public string id = "";
        public int value;
        public Item? next;
        public int postLoadInits;
        public bool nextWasResolvedAtPostLoad;

        public Item() { }

        public Item(string id, int value)
        {
            this.id = id;
            this.value = value;
        }

        public string GetUniqueLoadID() => "Item_" + id;

        public virtual void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref value, "value");
            Scribe_References.Look(ref next, "next");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                postLoadInits++;
                nextWasResolvedAtPostLoad = next != null;
            }
        }
    }

    public class SpecialItem : Item
    {
        public string extra = "";

        public SpecialItem() { }

        public SpecialItem(string id, int value, string extra) : base(id, value)
        {
            this.extra = extra;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref extra, "extra", "");
        }
    }

    public class Bag : IExposable
    {
        public int count;
        public float weight = 1.5f;
        public string? name;
        public bool flag;
        public TestKind kind;
        public IntRange range;
        public int? maybe;
        public List<int>? ints;
        public List<string>? strings;
        public List<Item>? items;
        public List<Item>? refs;
        public Dictionary<string, int>? dict;
        public Dictionary<string, Item>? refDict;
        public HashSet<string>? tags;
        public Item? owned;
        public Item? favorite;
        public TestDef? def;
        public List<TestDef>? defs;
        public int innerA;
        public int innerB;
        public readonly List<LoadSaveMode> modesSeen = new List<LoadSaveMode>();
        public int postLoadInits;
        public bool favoriteResolvedAtPostLoad;

        public void ExposeData()
        {
            if (Scribe.mode != LoadSaveMode.Saving) modesSeen.Add(Scribe.mode);
            Scribe_Values.Look(ref count, "count");
            Scribe_Values.Look(ref weight, "weight", 1.5f);
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref flag, "flag");
            Scribe_Values.Look(ref kind, "kind");
            Scribe_Values.Look(ref range, "range");
            Scribe_Values.Look(ref maybe, "maybe");
            Scribe_Collections.Look(ref ints, "ints");
            Scribe_Collections.Look(ref strings, "strings");
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            Scribe_Collections.Look(ref refs, "refs", LookMode.Reference);
            Scribe_Collections.Look(ref dict, "dict", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref refDict, "refDict", LookMode.Value, LookMode.Reference);
            Scribe_Collections.Look(ref tags, "tags");
            Scribe_Deep.Look(ref owned, "owned");
            Scribe_References.Look(ref favorite, "favorite");
            Scribe_Defs.Look(ref def, "def");
            Scribe_Collections.Look(ref defs, "defs", LookMode.Def);
            if (Scribe.EnterNode("inner"))
            {
                Scribe_Values.Look(ref innerA, "a");
                Scribe_Values.Look(ref innerB, "b");
                Scribe.ExitNode();
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                postLoadInits++;
                favoriteResolvedAtPostLoad = favorite != null;
            }
        }
    }

    public class ScribeTests
    {
        private static Bag FullBag()
        {
            var a = new Item("a", 1);
            var b = new Item("b", 2);
            var c = new SpecialItem("c", 3, "shiny");
            a.next = c;
            c.next = a;
            return new Bag
            {
                count = 4,
                weight = 2.25f,
                name = "  padded name ",
                flag = true,
                kind = TestKind.Beta,
                range = new IntRange(2, 9),
                maybe = 7,
                ints = new List<int> { 3, -1, 42 },
                strings = new List<string> { "x", "", "z" },
                items = new List<Item> { a, b, c },
                refs = new List<Item> { c, a },
                dict = new Dictionary<string, int> { { "one", 1 }, { "two", 2 } },
                refDict = new Dictionary<string, Item> { { "fav", b } },
                tags = new HashSet<string> { "t1", "t2" },
                owned = new SpecialItem("own", 9, "mine"),
                favorite = b,
                innerA = 5,
                innerB = 6,
            };
        }

        [Fact]
        public void Values_and_collections_round_trip()
        {
            Bag bag = FullBag();
            string xml = Scribe.SaveToString(bag, "bag");
            Bag loaded = Scribe.Load<Bag>(xml, "bag", out IReadOnlyList<string> errors);

            Assert.Empty(errors);
            Assert.Equal(4, loaded.count);
            Assert.Equal(2.25f, loaded.weight);
            Assert.Equal("  padded name ", loaded.name);
            Assert.True(loaded.flag);
            Assert.Equal(TestKind.Beta, loaded.kind);
            Assert.Equal(new IntRange(2, 9), loaded.range);
            Assert.Equal(7, loaded.maybe);
            Assert.Equal(new[] { 3, -1, 42 }, loaded.ints);
            Assert.Equal(new[] { "x", "", "z" }, loaded.strings);
            Assert.Equal(new Dictionary<string, int> { { "one", 1 }, { "two", 2 } }, loaded.dict);
            Assert.Equal(new HashSet<string> { "t1", "t2" }, loaded.tags);
            Assert.Equal(5, loaded.innerA);
            Assert.Equal(6, loaded.innerB);
        }

        [Fact]
        public void Deep_objects_keep_their_runtime_type()
        {
            string xml = Scribe.SaveToString(FullBag(), "bag");
            XDocument doc = XDocument.Parse(xml);
            Assert.Equal(typeof(SpecialItem).FullName, doc.Root!.Element("bag")!.Element("owned")!.Attribute("Class")!.Value);

            Bag loaded = Scribe.Load<Bag>(xml, "bag");
            var owned = Assert.IsType<SpecialItem>(loaded.owned);
            Assert.Equal("mine", owned.extra);
            Assert.Equal(9, owned.value);
            Assert.Equal(new[] { typeof(Item), typeof(Item), typeof(SpecialItem) }, loaded.items!.Select(i => i.GetType()));
        }

        [Fact]
        public void References_resolve_forward_backward_and_across_collections()
        {
            Bag loaded = Scribe.Load<Bag>(Scribe.SaveToString(FullBag(), "bag"), "bag", out IReadOnlyList<string> errors);
            Assert.Empty(errors);

            Item a = loaded.items![0], b = loaded.items[1], c = loaded.items[2];
            Assert.Same(c, a.next);
            Assert.Same(a, c.next);
            Assert.Null(b.next);
            Assert.Same(b, loaded.favorite);
            Assert.Equal(2, loaded.refs!.Count);
            Assert.Same(c, loaded.refs[0]);
            Assert.Same(a, loaded.refs[1]);
            Assert.Same(b, loaded.refDict!["fav"]);
        }

        [Fact]
        public void Defaults_are_omitted_from_the_save_and_restored_on_load()
        {
            var bag = new Bag();
            string xml = Scribe.SaveToString(bag, "bag");
            XElement root = XDocument.Parse(xml).Root!.Element("bag")!;
            Assert.Null(root.Element("count"));
            Assert.Null(root.Element("weight"));
            Assert.Null(root.Element("flag"));
            Assert.Null(root.Element("maybe"));

            Bag loaded = Scribe.Load<Bag>(xml, "bag", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(0, loaded.count);
            Assert.Equal(1.5f, loaded.weight);
            Assert.Null(loaded.maybe);
            Assert.Null(loaded.ints);
            Assert.Null(loaded.items);
            Assert.Null(loaded.owned);
            Assert.Null(loaded.favorite);
        }

        [Fact]
        public void Null_and_empty_collections_are_distinguished()
        {
            var bag = new Bag { ints = new List<int>(), items = new List<Item>(), strings = null };
            Bag loaded = Scribe.Load<Bag>(Scribe.SaveToString(bag, "bag"), "bag");
            Assert.NotNull(loaded.ints);
            Assert.Empty(loaded.ints!);
            Assert.NotNull(loaded.items);
            Assert.Empty(loaded.items!);
            Assert.Null(loaded.strings);
        }

        [Fact]
        public void Unresolved_reference_is_reported_and_left_null()
        {
            string xml = @"<savegame><bag><items><li><id>a</id><value>1</value><next>Item_missing</next></li></items></bag></savegame>";
            Bag loaded = Scribe.Load<Bag>(xml, "bag", out IReadOnlyList<string> errors);
            string error = Assert.Single(errors);
            Assert.Contains("Item_missing", error);
            Assert.Null(loaded.items![0].next);
        }

        [Fact]
        public void Defs_save_by_name_and_load_from_the_given_database()
        {
            DefLoadResult defs = TestLoader.Load("<Defs><TestDef><defName>Alpha</defName></TestDef><TestDef><defName>Beta</defName></TestDef></Defs>");
            DefDatabase db = new DefDatabase();
            foreach (Def d in defs.Defs) db.Add(d);
            var alpha = db.GetNamed<TestDef>("Alpha");
            var beta = db.GetNamed<TestDef>("Beta");

            var bag = new Bag { def = beta, defs = new List<TestDef> { alpha, beta } };
            string xml = Scribe.SaveToString(bag, "bag");
            Assert.Contains("<def>Beta</def>", xml);

            Bag loaded = Scribe.Load<Bag>(xml, "bag", out IReadOnlyList<string> errors, db);
            Assert.Empty(errors);
            Assert.Same(beta, loaded.def);
            Assert.Equal(new[] { alpha, beta }, loaded.defs);

            Bag missing = Scribe.Load<Bag>(xml.Replace("Beta", "Gone"), "bag", out errors, db);
            Assert.Equal(2, errors.Count);
            Assert.Null(missing.def);
        }

        [Fact]
        public void Load_runs_three_passes_and_PostLoadInit_sees_resolved_references()
        {
            Bag loaded = Scribe.Load<Bag>(Scribe.SaveToString(FullBag(), "bag"), "bag");
            Assert.Equal(new[] { LoadSaveMode.LoadingVars, LoadSaveMode.ResolvingCrossRefs, LoadSaveMode.PostLoadInit }, loaded.modesSeen);
            Assert.Equal(1, loaded.postLoadInits);
            Assert.True(loaded.favoriteResolvedAtPostLoad);
            Assert.All(loaded.items!, i => Assert.Equal(1, i.postLoadInits));
            Assert.True(loaded.items![0].nextWasResolvedAtPostLoad);
            Assert.Equal(1, loaded.owned!.postLoadInits);
        }

        [Fact]
        public void Missing_group_node_leaves_defaults()
        {
            Bag loaded = Scribe.Load<Bag>("<savegame><bag><count>2</count></bag></savegame>", "bag", out IReadOnlyList<string> errors);
            Assert.Empty(errors);
            Assert.Equal(2, loaded.count);
            Assert.Equal(0, loaded.innerA);
        }

        [Fact]
        public void Missing_root_and_bad_xml_throw()
        {
            Assert.Throws<ScribeException>(() => Scribe.Load<Bag>("<savegame></savegame>", "bag"));
            Assert.Throws<ScribeException>(() => Scribe.Load<Bag>("<savegame>", "bag"));
            Assert.Equal(LoadSaveMode.Inactive, Scribe.mode);
        }

        [Fact]
        public void Duplicate_load_ids_are_reported()
        {
            var bag = new Bag { items = new List<Item> { new Item("dup", 1), new Item("dup", 2) } };
            Scribe.Load<Bag>(Scribe.SaveToString(bag, "bag"), "bag", out IReadOnlyList<string> errors);
            string error = Assert.Single(errors);
            Assert.Contains("Item_dup", error);
        }

        [Fact]
        public void Bad_value_text_is_an_error_and_keeps_the_default()
        {
            Bag loaded = Scribe.Load<Bag>("<savegame><bag><count>abc</count><weight>3</weight></bag></savegame>", "bag", out IReadOnlyList<string> errors);
            Assert.Single(errors);
            Assert.Equal(0, loaded.count);
            Assert.Equal(3f, loaded.weight);
        }

        [Fact]
        public void A_second_session_on_the_same_thread_is_rejected()
        {
            Scribe.saver.InitSaving("savegame");
            try
            {
                Assert.Throws<ScribeException>(() => Scribe.SaveToString(new Bag(), "bag"));
            }
            finally
            {
                Scribe.ForceStop();
            }
            Assert.Equal(LoadSaveMode.Inactive, Scribe.mode);
            Assert.Equal(4, Scribe.Load<Bag>(Scribe.SaveToString(FullBag(), "bag"), "bag").count);
        }

        [Fact]
        public void Sessions_are_isolated_per_thread()
        {
            var results = new int[16];
            Parallel.For(0, results.Length, i =>
            {
                var bag = new Bag { count = i, items = new List<Item> { new Item("i" + i, i) } };
                bag.favorite = bag.items[0];
                for (int round = 0; round < 20; round++)
                {
                    Bag loaded = Scribe.Load<Bag>(Scribe.SaveToString(bag, "bag"), "bag", out IReadOnlyList<string> errors);
                    if (errors.Count != 0 || loaded.count != i || !ReferenceEquals(loaded.favorite, loaded.items![0]))
                    {
                        results[i] = -1;
                        return;
                    }
                }
                results[i] = 1;
            });
            Assert.All(results, r => Assert.Equal(1, r));
        }
    }
}
