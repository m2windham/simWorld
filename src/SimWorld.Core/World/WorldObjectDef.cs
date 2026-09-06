using System;
using SimWorld.Defs;

namespace SimWorld.World
{
    /// <summary>A kind of thing that can sit on a world tile (RimWorld: <c>RimWorld.Planet.WorldObjectDef</c>). Only <c>Settlement</c> exists so far.</summary>
    public class WorldObjectDef : Def
    {
        public Type worldObjectClass = typeof(WorldObject);
    }
}
