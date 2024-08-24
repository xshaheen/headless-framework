using System.Reflection;
using Framework.BuildingBlocks.Primitives.Extensions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Framework.Api.Swagger.Swashbuckle.Extensions;

/// <summary>A static class providing methods to configure Swagger mappings for Primitive types.</summary>
public static class SwashbuckleSwaggerGenOptionsExtensions
{
    private const string _TypeName = "AddSwashbuckleSwaggerPrimitivesMappingsExtensions";
    private const string _MethodName = "AddSwaggerPrimitiveMappings";

    /// <summary>Adds Swagger mappings for all Primitive types to the specified SwaggerGenOptions.</summary>
    public static void AddPrimitivesSwaggerMappings(this SwaggerGenOptions options, params Assembly[] assemblies)
    {
        PrimitiveInvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, options);
    }

    /// <summary>Adds Swagger mappings for all Primitive types to the specified SwaggerGenOptions.</summary>
    public static void AddAllPrimitivesSwaggerMappings(this SwaggerGenOptions options)
    {
        PrimitiveInvokeHelper.InvokeInAllPrimitiveAssemblies(_TypeName, _MethodName, options);
    }
}
