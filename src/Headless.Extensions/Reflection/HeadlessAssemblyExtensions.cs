// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Reflection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System.Reflection;

/// <summary>
/// Extension methods over <see cref="Assembly"/> for reading assembly metadata attributes and enumerating types.
/// </summary>
[PublicAPI]
public static class HeadlessAssemblyExtensions
{
    #region Assembly Information

    /// <summary>Gets the title declared by the assembly's <see cref="AssemblyTitleAttribute"/>.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The assembly title, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetAssemblyTitle(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
    }

    /// <summary>Gets the product name declared by the assembly's <see cref="AssemblyProductAttribute"/>.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The product name, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetAssemblyProduct(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
    }

    /// <summary>Gets the description declared by the assembly's <see cref="AssemblyDescriptionAttribute"/>.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The assembly description, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetAssemblyDescription(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
    }

    /// <summary>Gets the company declared by the assembly's <see cref="AssemblyCompanyAttribute"/>.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The company name, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetAssemblyCompany(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
    }

    /// <summary>Gets the file version declared by the assembly's <see cref="AssemblyFileVersionAttribute"/>.</summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The file version string, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetAssemblyVersion(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
    }

    /// <summary>
    /// Gets the informational version declared by the assembly's <see cref="AssemblyInformationalVersionAttribute"/>,
    /// which typically embeds the source-control commit (for example <c>1.2.3+abcdef</c>).
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The informational version string, or <see langword="null"/> when the attribute is not present.</returns>
    public static string? GetCommitVersion(this Assembly assembly)
    {
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
    }

    /// <summary>
    /// Determines whether the assembly's full name belongs to the <c>System.*</c> or <c>Microsoft.*</c> namespaces.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns><see langword="true"/> if the assembly's full name starts with <c>System.</c> or <c>Microsoft.</c>; otherwise <see langword="false"/>.</returns>
    public static bool IsSystemAssemblyName(this Assembly assembly)
    {
        return AssemblyHelper.IsSystemAssemblyName(assembly.FullName);
    }

    /// <summary>
    /// Determines whether the assembly name belongs to the <c>System.*</c> or <c>Microsoft.*</c> namespaces.
    /// </summary>
    /// <param name="assemblyName">The assembly name to inspect.</param>
    /// <returns><see langword="true"/> if the full name starts with <c>System.</c> or <c>Microsoft.</c>; otherwise <see langword="false"/>.</returns>
    public static bool IsSystemAssemblyName(this AssemblyName assemblyName)
    {
        return AssemblyHelper.IsSystemAssemblyName(assemblyName.FullName);
    }

    #endregion

    #region Get Assembly Types

    /// <summary>
    /// Gets the loadable types of the assembly that can be constructed (non-abstract and not open generic definitions).
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The constructible types. Types that fail to load are silently skipped (see <see cref="GetLoadableTypes"/>).</returns>
    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static IEnumerable<Type> GetConstructibleTypes(this Assembly assembly)
    {
        return assembly.GetLoadableTypes().Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    /// <summary>
    /// Gets all types defined in the assembly, tolerating partial load failures by returning only the types that
    /// could be loaded instead of throwing.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The successfully loaded types. Types that failed to load (when a <see cref="ReflectionTypeLoadException"/> is raised internally) are excluded.</returns>
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

    /// <summary>
    /// Gets the loadable defined types of the assembly that can be constructed (non-abstract and not open generic definitions).
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The constructible defined types. Types that fail to load are silently skipped (see <see cref="GetLoadableDefinedTypes"/>).</returns>
    [MustUseReturnValue]
    [RequiresUnreferencedCode("Gets types from the given assembly - unsafe for trimming")]
    public static IEnumerable<TypeInfo> GetConstructibleDefinedTypes(this Assembly assembly)
    {
        return assembly
            .GetLoadableDefinedTypes()
            .Where(t => t is { IsAbstract: false, IsGenericTypeDefinition: false });
    }

    /// <summary>
    /// Gets all defined types of the assembly, tolerating partial load failures by returning only the types that
    /// could be loaded instead of throwing.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>The successfully loaded defined types. Types that failed to load (when a <see cref="ReflectionTypeLoadException"/> is raised internally) are excluded.</returns>
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
