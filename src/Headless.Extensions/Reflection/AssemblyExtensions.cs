// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Reflection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace System.Reflection;

public static class AssemblyExtensions
{
    #region Assembly Information

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

    public static bool IsSystemAssemblyName(this Assembly assembly)
    {
        return AssemblyHelper.IsSystemAssemblyName(assembly.FullName);
    }

    public static bool IsSystemAssemblyName(this AssemblyName assemblyName)
    {
        return AssemblyHelper.IsSystemAssemblyName(assemblyName.FullName);
    }

    #endregion

    #region Get Assembly Types

    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static IEnumerable<Type> GetConstructibleTypes(this Assembly assembly)
    {
        return assembly.GetLoadableTypes().Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static Type[] GetLoadableTypes(this Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null).ToArray()!;
        }
    }

    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static IEnumerable<TypeInfo> GetConstructibleDefinedTypes(this Assembly assembly)
    {
        return assembly
            .GetLoadableDefinedTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static IEnumerable<TypeInfo> GetLoadableDefinedTypes(this Assembly assembly)
    {
        try
        {
            return assembly.DefinedTypes;
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Select(IntrospectionExtensions.GetTypeInfo!);
        }
    }

    #endregion
}
