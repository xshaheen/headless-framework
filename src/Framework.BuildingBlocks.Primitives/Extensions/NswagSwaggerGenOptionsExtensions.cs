using System.Reflection;
using NJsonSchema.Generation;
using Primitives;

namespace Framework.BuildingBlocks.Primitives.Extensions;

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
        InvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified JsonSchemaGeneratorSettings.</summary>
    public static void AddAllPrimitivesSwaggerMappings(this JsonSchemaGeneratorSettings options)
    {
        InvokeHelper.InvokeInAllPrimitiveAssemblies(_TypeName, _MethodName, options);
    }
}
