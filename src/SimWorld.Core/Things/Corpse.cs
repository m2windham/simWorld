using System;
using SimWorld.Map;
using SimWorld.Pawns;
using SimWorld.Sim;

namespace SimWorld.Things
{
    /// <summary>
    /// A dead pawn's body, as a Thing on the map (RimWorld: <c>Verse.Corpse</c>). The pawn itself is not
    /// destroyed when it dies — it is taken off the map and held here, so everything that still names a dead
    /// person (the chronicle, the family tree, a relative's bereavement) keeps pointing at the same
    /// <see cref="Pawn"/> object it always did, while the map gains something haulable, rottable and
    /// butcherable that was not there before.
    /// <para/>
    /// <b>Holding, without a ThingOwner.</b> RimWorld's Corpse owns its pawn through a
    /// <c>ThingOwner</c>/<c>IThingHolder</c> pair, and <c>Pawn.Corpse</c> reads the holder back out. This
    /// port has no container layer at all (nothing anywhere holds a Thing inside another Thing), so the link
    /// is a plain pair of references: <see cref="InnerPawn"/> here and <see cref="Pawn.corpse"/> back. The
    /// back-reference is maintained by this class alone and is never Scribed — a load re-establishes it from
    /// this side (see <see cref="ExposeData"/>), so the two halves cannot drift apart in a save.
    /// <para/>
    /// <b>Destroying a corpse destroys the body.</b> Butchering is the one thing in this port that does it,
    /// and it must leave no orphan: a Pawn still reachable from a family tree but neither spawned nor inside
    /// a corpse is exactly the "lingering dead pawn" this class exists to end. <see cref="Destroy"/>
    /// therefore takes the inner pawn with it, which is also what keeps <c>Pawn.Destroyed</c> meaning what
    /// callers written before corpses existed already assumed it meant.
    /// </summary>
    public class Corpse : ThingWithComps
    {
        private Pawn? innerPawn;

        /// <summary>Tick the pawn died on (RimWorld: <c>Corpse.timeOfDeath</c>); <see cref="Age"/> reads it.</summary>
        public int timeOfDeath;

        /// <summary>
        /// The body this corpse holds. Setting it also points the pawn back at this corpse
        /// (<see cref="Pawn.corpse"/>), which is the only way that field is ever written.
        /// </summary>
        public Pawn? InnerPawn
        {
            get => innerPawn;
            set
            {
                if (innerPawn != null && ReferenceEquals(innerPawn.corpse, this)) innerPawn.corpse = null;
                innerPawn = value;
                if (value != null) value.corpse = this;
            }
        }

        /// <summary>Ticks since death; 0 before the clock has moved (and for a corpse made with no clock).</summary>
        public int Age
        {
            get
            {
                int now = Find.TickManager?.TicksGame ?? 0;
                return now > timeOfDeath ? now - timeOfDeath : 0;
            }
        }

        public CompRottable? RotComp => GetComp<CompRottable>();

        /// <summary>How far gone this body is. A corpse with no <see cref="CompRottable"/> (a non-flesh race)
        /// is always <see cref="RotStage.Fresh"/> — it has nothing to rot.</summary>
        public RotStage RotStageNow => RotComp?.Stage ?? RotStage.Fresh;

        /// <summary>
        /// Fresh enough to be worth butchering. RimWorld gates butchery products on the same stage
        /// distinction (a dessicated corpse yields nothing); this port makes the gate the offer itself, so a
        /// pawn never walks across the map to butcher a husk and come away with nothing.
        /// </summary>
        public bool IsButcherable => InnerPawn != null && InnerPawn.RaceProps.Animal && RotStageNow != RotStage.Dessicated;

        /// <summary>"Bob's corpse" for a pawn with a name, else the def's own "dead husky". A label choice,
        /// not a mechanic: RimWorld reads the same two cases out of one translation key.</summary>
        public override string Label
        {
            get
            {
                Pawn? pawn = innerPawn;
                if (pawn?.Name != null) return pawn.Name.ToStringShort + "'s corpse";
                return def?.label ?? base.Label;
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            Pawn? pawn = innerPawn;
            base.Destroy(mode);
            if (pawn != null && !pawn.Destroyed) pawn.Destroy(mode);
        }

        public override void ExposeData()
        {
            // A save written before this corpse's ThingDef was generated is impossible (the corpse could not
            // have existed), but a *load* into a fresh process reaches this before anything has asked for a
            // corpse def — and Thing.ExposeData below resolves `def` by name. Generating first is what makes
            // a corpse survive a round trip through a database that has only just finished loading content.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                CorpseDefGenerator.EnsureGenerated(Scribe.loader.Defs);
            }

            base.ExposeData();
            Scribe_Values.Look(ref timeOfDeath, "timeOfDeath");

            Pawn? pawn = innerPawn;
            Scribe_Deep.Look(ref pawn, "innerPawn");
            if (Scribe.mode != LoadSaveMode.Saving) InnerPawn = pawn;
        }

        public override string ToString() => Label + " (" + ThingID + ")";
    }

    /// <summary>
    /// Makes the body a death leaves behind (RimWorld: <c>Pawn.MakeCorpse</c>, called from <c>Pawn.Kill</c>).
    /// Reached from <see cref="Pawn.Notify_Died"/> — the one funnel every death in this port already passes
    /// through, whatever killed the pawn (age, a hediff, a failed surgery, a hunter's arrow).
    /// </summary>
    public static class CorpseMaker
    {
        /// <summary>
        /// Takes <paramref name="pawn"/> off its map and leaves a <see cref="Corpse"/> holding it on the cell
        /// it died on. Returns null — leaving the pawn exactly as it was — when the pawn is not dead, when it
        /// already has a corpse (so a second call is a no-op rather than a second body), or when it was not
        /// spawned at all: a pawn that dies with no map has no cell to leave a body on. That last case is the
        /// ordinary one for this port's non-Full citizens, who are dead-and-unspawned and simply drop off
        /// their settlement's roster (<c>World.Settlement.SyncCitizenSpawns</c>) as they always did.
        /// </summary>
        public static Corpse? MakeAndSpawnCorpseFor(Pawn pawn)
        {
            if (pawn == null) throw new ArgumentNullException(nameof(pawn));
            if (!pawn.Dead || pawn.Destroyed) return null;
            if (pawn.corpse != null) return pawn.corpse;

            Map.Map? map = pawn.Map;
            if (map == null) return null;

            IntVec3 deathCell = pawn.Position;
            Defs.ThingDef corpseDef = CorpseDefGenerator.CorpseDefFor(pawn.def);
            var corpse = (Corpse)ThingMaker.MakeThing(corpseDef);
            corpse.InnerPawn = pawn;
            corpse.timeOfDeath = Find.TickManager?.TicksGame ?? 0;

            pawn.DeSpawn();
            GenSpawn.Spawn(corpse, deathCell, map);
            return corpse;
        }
    }
}
