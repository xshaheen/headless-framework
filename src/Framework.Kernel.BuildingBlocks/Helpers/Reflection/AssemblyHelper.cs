// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Reflection;
using System.Runtime.Loader;
using MoreLinq;

namespace Framework.Kernel.BuildingBlocks.Helpers.Reflection;

[PublicAPI]
public static class AssemblyHelper
{
    public static List<Assembly> LoadAssemblies(string folderPath, SearchOption searchOption)
    {
        return GetAssemblyFiles(folderPath, searchOption)
            .Select(item => AssemblyLoadContext.Default.LoadFromAssemblyPath(item))
            .ToList();
    }

    public static IEnumerable<string> GetAssemblyFiles(string folderPath, SearchOption searchOption)
    {
        return Directory
            .EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(s =>
                s.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            );
    }

    public static HashSet<Assembly> GetCurrentAssemblies(
        Func<Assembly, bool> acceptPredicate,
        Func<string, bool> excludePredicate
    )
    {
        var currentlyLoadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        var (excluded, included) = currentlyLoadedAssemblies.Partition(assembly =>
            excludePredicate(assembly.FullName!)
        );

        // Put all the exclude assemblies as checked
        HashSet<string> referencesCheckedNames = new(excluded.Select(a => a.FullName!), StringComparer.Ordinal);
        Queue<Assembly> assembliesToCheck = new(included);
        HashSet<Assembly> acceptedAssemblies = [];

        while (assembliesToCheck.Count > 0)
        {
            var assembly = assembliesToCheck.Dequeue();

            if (acceptPredicate(assembly))
            {
                var added = acceptedAssemblies.Add(assembly);

                if (!added)
                {
                    continue; // Already processed
                }
            }

            // Check all the references of the assembly
            foreach (var reference in assembly.GetReferencedAssemblies())
            {
                if (referencesCheckedNames.Contains(reference.FullName) || excludePredicate(reference.FullName))
                {
                    continue;
                }

                var loadedAssembly = Assembly.Load(reference);
                assembliesToCheck.Enqueue(loadedAssembly);
                referencesCheckedNames.Add(reference.FullName);
            }
        }

        return acceptedAssemblies;
    }

    public static bool IsSystemAssemblyName(string? assemblyFullName)
    {
        return assemblyFullName is not null
            && (
                assemblyFullName.StartsWith("System.", StringComparison.Ordinal)
                || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal)
            );
    }
}
