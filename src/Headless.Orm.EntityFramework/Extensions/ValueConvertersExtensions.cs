// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Generator.Primitives;
using Headless.Reflection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>A static class providing methods to configure EF value converts for Primitive types.</summary>
[PublicAPI]
public static class ValueConvertersExtensions
{
    private const string _TypeName = "AddPrimitivesValueConvertersExtensions";
    private const string _MethodName = "AddPrimitivePropertyConversions";

    /// <summary>Adds Value converters for all Primitive types to the specified ModelConfigurationBuilder.</summary>
    public static void AddPrimitivesValueConvertersMappings(
        this ModelConfigurationBuilder configurationBuilder,
        params Assembly[] assemblies
    )
    {
        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: configurationBuilder);
    }

    /// <summary>Adds Value converters for all Primitive types to the specified ModelConfigurationBuilder.</summary>
    public static void AddAllPrimitivesValueConvertersMappings(this ModelConfigurationBuilder builder)
    {
        var assemblies = AssemblyHelper.GetCurrentAssemblies(
            acceptPredicate: assembly => assembly.GetCustomAttribute<PrimitiveAssemblyAttribute>() is not null,
            excludePredicate: AssemblyHelper.IsSystemAssemblyName
        );

        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: builder);
    }

    /// <summary>Adds Value converters for all Primitive types to the specified ModelConfigurationBuilder.</summary>
    public static void AddAllPrimitivesValueConvertersMappings(
        this ModelConfigurationBuilder builder,
        IEnumerable<Assembly> assemblies
    )
    {
        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: builder);
    }
}
