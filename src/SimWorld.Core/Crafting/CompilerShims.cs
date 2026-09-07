namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Compiler-recognized marker the C# 9 <c>init</c> accessor and positional <c>record</c> types need;
    /// shipped in the BCL from .NET 5 on, but missing from netstandard2.1, so this polyfill defines it for
    /// <see cref="SimWorld.Crafting.ItemStack"/>. Harmless if another module adds the identical polyfill —
    /// remove the duplicate, not both, since removing this one would break ItemStack.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
