namespace SimWorld.Noise
{
    /// <summary>
    /// A 3D scalar field, sampled at a point (RimWorld/libnoise: <c>Verse.Noise.ModuleBase</c>). Modules
    /// compose: a <see cref="Perlin"/> or <see cref="RidgedMultifractal"/> generator feeds combinators
    /// (<see cref="Add"/>, <see cref="Multiply"/>, <see cref="ScaleBias"/>, <see cref="Clamp"/>, <see cref="Abs"/>,
    /// <see cref="Invert"/>) to build the terrain/rainfall/temperature fields in <c>SimWorld.World.Gen</c>.
    /// Double precision throughout, matching libnoise.
    /// </summary>
    public abstract class ModuleBase
    {
        public abstract double GetValue(double x, double y, double z);
    }
}
