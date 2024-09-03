using System.Reflection;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

/// <summary>A static class providing methods to configure EF value converts for Primitive types.</summary>
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
        PrimitiveInvokeHelper.InvokeInAssemblies(assemblies, _TypeName, _MethodName, configurationBuilder);
    }

    /// <summary>Adds Value converters for all Primitive types to the specified ModelConfigurationBuilder.</summary>
    public static void AddAllPrimitivesValueConvertersMappings(this ModelConfigurationBuilder configurationBuilder)
    {
        PrimitiveInvokeHelper.InvokeInAllPrimitiveAssemblies(_TypeName, _MethodName, configurationBuilder);
    }
}
