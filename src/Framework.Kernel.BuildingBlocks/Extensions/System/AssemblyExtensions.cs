// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

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

    public static bool HsSystemAssemblyName(this Assembly assembly)
    {
        return assembly.FullName is not null
            && (
                assembly.FullName.StartsWith("System.", StringComparison.Ordinal)
                || assembly.FullName.StartsWith("Microsoft.", StringComparison.Ordinal)
            );
    }

    public static bool HsSystemAssemblyName(this AssemblyName assemblyName)
    {
        return assemblyName.FullName.StartsWith("System.", StringComparison.Ordinal)
            || assemblyName.FullName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }
}
