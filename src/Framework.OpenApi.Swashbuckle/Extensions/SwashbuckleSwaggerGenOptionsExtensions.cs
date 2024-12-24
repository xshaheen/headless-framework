// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Framework.Generator.Primitives;
using Framework.Reflection;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.OpenApi.Swashbuckle.Extensions;

/// <summary>A static class providing methods to configure Swagger mappings for Primitive types.</summary>
[PublicAPI]
public static class SwashbuckleSwaggerGenOptionsExtensions
{
    private const string _TypeName = "AddSwashbuckleSwaggerPrimitivesMappingsExtensions";
    private const string _MethodName = "AddSwaggerPrimitiveMappings";

    /// <summary>Adds Swagger mappings for all Primitive types to the specified SwaggerGenOptions.</summary>
    public static void AddPrimitivesSwaggerMappings(this SwaggerGenOptions options, params Assembly[] assemblies)
    {
        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified SwaggerGenOptions.</summary>
    public static void AddAllPrimitivesSwaggerMappings(this SwaggerGenOptions options)
    {
        var assemblies = AssemblyHelper.GetCurrentAssemblies(
            acceptPredicate: assembly => assembly.GetCustomAttribute<PrimitiveAssemblyAttribute>() is not null,
            excludePredicate: AssemblyHelper.IsSystemAssemblyName
        );

        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified SwaggerGenOptions.</summary>
    public static void AddAllPrimitivesSwaggerMappings(this SwaggerGenOptions options, IEnumerable<Assembly> assemblies)
    {
        assemblies.InvokeAllStaticMethods(_TypeName, _MethodName, parameters: options);
    }
}
