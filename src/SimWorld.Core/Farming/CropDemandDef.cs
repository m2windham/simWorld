using System;
using System.Collections.Generic;

using SimWorld.Defs;

namespace SimWorld.Farming
{
    /// <summary>
    /// A crop a settlement grows for something other than food: its own field, sized by its own worker
    /// (crafting.billcreators). <c>Building.FarmingInitiative</c> grows one food field and picks its crop by
    /// nutrition, so a fibre or a medicine crop never wins there; each of those ships one of these instead,
    /// and <see cref="CropDemandFields"/> keeps its field. Cotton for cloth is the first
    /// (<c>CropDemands_Cloth.xml</c>); healroot for medicine fits the same shape with a worker of its own.
    ///
    /// <para/>This port's own: RimWorld's player draws every growing zone by hand and picks its plant, so there
    /// is nothing to port. The field label is how the field is found again, the same way
    /// <c>FarmingInitiative.FieldLabel</c> finds the food field.
    /// </summary>
    public class CropDemandDef : Def
    {
        /// <summary>The plant sown in this field.</summary>
        public ThingDef crop = null!;

        /// <summary>The growing zone's label, unique per def.</summary>
        public string fieldLabel = "";

        /// <summary>How many cells the field wants (<see cref="CropDemandWorker"/>).</summary>
        public Type workerClass = typeof(CropDemandWorker);

        private CropDemandWorker? workerInt;

        /// <summary>Lazily constructed, cached worker instance (same pattern as <c>EdictDef.Worker</c>).</summary>
        public CropDemandWorker Worker
        {
            get
            {
                if (workerInt == null)
                {
                    workerInt = (CropDemandWorker)Activator.CreateInstance(workerClass)!;
                    workerInt.def = this;
                }
                return workerInt;
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (crop?.plant == null) yield return "crop must be a plant.";
            if (string.IsNullOrEmpty(fieldLabel)) yield return "fieldLabel is required.";
            if (!typeof(CropDemandWorker).IsAssignableFrom(workerClass))
            {
                yield return "workerClass must derive from CropDemandWorker.";
            }
        }
    }
}
