#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Reflection;

public static class AssemblyExtensions
{
    public static string? GetAssemblyProduct(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
    }

    public static string? GetAssemblyDescription(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
    }

    public static string? GetAssemblyVersion(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
    }
}
