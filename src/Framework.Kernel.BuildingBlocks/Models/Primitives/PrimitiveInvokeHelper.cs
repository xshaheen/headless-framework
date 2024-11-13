// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public static class PrimitiveInvokeHelper
{
    public static void InvokeInAssemblies(
        IEnumerable<Assembly> assemblies,
        string typeName,
        string methodName,
        object methodParameter
    )
    {
        foreach (var assembly in assemblies)
        {
            _ProcessAssembly(assembly, typeName, methodName, methodParameter);
        }
    }

    private static void _ProcessAssembly(
        Assembly assembly,
        string typeName,
        string methodName,
        object extensionsParameter
    )
    {
        var methods = assembly
            .GetExportedTypes()
            .Where(type => type.IsPublic && string.Equals(type.Name, typeName, StringComparison.Ordinal))
            .Select(type => type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static));

        // Calls the AddSwaggerMappings method for each type.
        foreach (var method in methods)
        {
            method?.Invoke(null, [extensionsParameter]);
        }
    }
}
