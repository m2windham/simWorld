namespace SimWorld.Economy
{
    /// <summary>How favorable a particular trade good's price is, before the buy/sell direction is applied (RimWorld: <c>RimWorld.PriceType</c>).</summary>
    public enum PriceType
    {
        Undefined,
        VeryCheap,
        Cheap,
        Normal,
        Expensive,
        Exorbitant,
    }

    /// <summary>RimWorld: <c>RimWorld.PriceUtility</c>'s price-type multiplier table.</summary>
    public static class PriceTypeUtility
    {
        public static float PriceMultiplier(PriceType priceType)
        {
            switch (priceType)
            {
                case PriceType.VeryCheap: return 0.4f;
                case PriceType.Cheap: return 0.7f;
                case PriceType.Normal: return 1f;
                case PriceType.Expensive: return 2f;
                case PriceType.Exorbitant: return 5f;
                default: return 1f;
            }
        }
    }
}
