#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Reflection;

public static class AssemblyExtensions
{
    public static string? GetAssemblyTitle(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
    }

    public static string? GetAssemblyProduct(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
    }

    public static string? GetAssemblyDescription(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
    }

    public static string? GetAssemblyCompany(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
    }

    public static string? GetAssemblyVersion(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
    }

    public static string? GetCommitVersion(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }
}
