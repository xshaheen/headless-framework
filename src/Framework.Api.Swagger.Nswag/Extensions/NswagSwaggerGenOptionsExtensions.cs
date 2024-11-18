// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.BuildingBlocks.Helpers.Reflection;
using Framework.Generator.Primitives;
using Framework.Primitives;
using NJsonSchema.Generation;

namespace Framework.Api.Swagger.Nswag.Extensions;

/// <summary>A static class providing methods to configure Swagger mappings for Primitive types.</summary>
public static class NswagSwaggerGenOptionsExtensions
{
    private const string _TypeName = "AddSwashbuckleSwaggerPrimitivesMappingsExtensions";
    private const string _MethodName = "AddSwaggerPrimitiveMappings";

    /// <summary>Adds Swagger mappings for all Primitive types to the specified JsonSchemaGeneratorSettings.</summary>
    public static void AddPrimitivesSwaggerMappings(
        this JsonSchemaGeneratorSettings options,
        params Assembly[] assemblies
    )
    {
        PrimitiveInvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified JsonSchemaGeneratorSettings.</summary>
    public static void AddAllPrimitivesSwaggerMappings(this JsonSchemaGeneratorSettings options)
    {
        var assemblies = AssemblyHelper.GetCurrentAssemblies(
            acceptPredicate: assembly => assembly.GetCustomAttribute<PrimitiveAssemblyAttribute>() is not null,
            excludePredicate: AssemblyHelper.IsSystemAssemblyName
        );

        PrimitiveInvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified JsonSchemaGeneratorSettings.</summary>
    public static void AddAllPrimitivesSwaggerMappings(
        this JsonSchemaGeneratorSettings options,
        IEnumerable<Assembly> assemblies
    )
    {
        PrimitiveInvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, options);
    }
}
