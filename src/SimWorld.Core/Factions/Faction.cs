using SimWorld.Defs;
using SimWorld.Sim;

namespace SimWorld.Factions
{
    /// <summary>
    /// One civilization instance (RimWorld: <c>RimWorld.Faction</c>), minimal for world generation:
    /// enough identity for settlements to belong to it and for saves to reference it.
    /// Relations, memes and ideology land with the full Factions system.
    /// </summary>
    public class Faction : IExposable, ILoadReferenceable
    {
        public FactionDef def = null!;
        public string name = "";
        public string loadID = "";

        /// <summary>For Scribe's deep-load construction.</summary>
        public Faction()
        {
        }

        public Faction(FactionDef def, string name, string loadID)
        {
            this.def = def;
            this.name = name;
            this.loadID = loadID;
        }

        public string GetUniqueLoadID() => loadID;

        public void ExposeData()
        {
            FactionDef? d = def;
            Scribe_Defs.Look(ref d, "def");
            def = d!;
            Scribe_Values.Look(ref name, "name", "");
            Scribe_Values.Look(ref loadID, "loadID", "");
        }

        public override string ToString() => name;
    }
}
