using System.Reflection;
using Primitives;

namespace Framework.BuildingBlocks.Primitives.Extensions;

internal static class InvokeHelper
{
    internal static void InvokeInAllPrimitiveAssemblies(string typeName, string methodName, object methodParameter)
    {
        var loadedAssemblies = new HashSet<string>(
            AppDomain.CurrentDomain.GetAssemblies().Where(a => _IsSystemAssembly(a.FullName)).Select(a => a.FullName!),
            StringComparer.Ordinal
        );

        var assembliesToCheck = new Queue<Assembly>(AppDomain.CurrentDomain.GetAssemblies());
        var processedPrimitiveAssemblies = new HashSet<Assembly>();

        while (assembliesToCheck.Count > 0)
        {
            var assembly = assembliesToCheck.Dequeue();

            if (
                !processedPrimitiveAssemblies.Contains(assembly)
                && assembly.GetCustomAttribute<PrimitiveAssemblyAttribute>() is not null
            )
            {
                _ProcessAssembly(assembly, typeName, methodName, methodParameter);
                processedPrimitiveAssemblies.Add(assembly);
            }

            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (loadedAssemblies.Contains(reference.FullName) || _IsSystemAssembly(reference.FullName))
                {
                    continue;
                }

                var loadedAssembly = Assembly.Load(reference);
                assembliesToCheck.Enqueue(loadedAssembly);
                loadedAssemblies.Add(reference.FullName);
            }
        }
    }

    internal static void InvokeInAssemblies(
        Assembly[] assemblies,
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

    private static bool _IsSystemAssembly(string? assemblyFullName)
    {
        return assemblyFullName?.StartsWith("System.", StringComparison.Ordinal) != false
            || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal);
    }
}
