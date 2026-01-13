// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using MoreLinq;

namespace Framework.Reflection;

[PublicAPI]
public static class AssemblyHelper
{
    public static bool IsSystemAssemblyName(string? assemblyFullName)
    {
        return assemblyFullName is not null
            && (
                assemblyFullName.StartsWith("System.", StringComparison.Ordinal)
                || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal)
            );
    }

    #region Get Assemblies From Folder

    [RequiresUnreferencedCode("Loading assemblies from path might load types that cannot be statically analyzed.")]
    public static List<Assembly> LoadAssemblies(string folderPath, SearchOption searchOption)
    {
        return GetAssemblyFiles(folderPath, searchOption)
            .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)
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

    #endregion

    #region Get Domain Assemblies

    [RequiresUnreferencedCode("Assembly scanning is not compatible with trimming.")]
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

    #endregion

    #region Invoke Static Methods

    [RequiresUnreferencedCode("Invokes methods by name using reflection, which is not compatible with trimming.")]
    public static void InvokeAllStaticMethods(
        this IEnumerable<Assembly> assemblies,
        string typeName,
        string methodName,
        params object?[]? parameters
    )
    {
        foreach (var assembly in assemblies)
        {
            InvokeAllStaticMethods(assembly, typeName, methodName, parameters);
        }
    }

    [RequiresUnreferencedCode("Invokes methods by name using reflection, which is not compatible with trimming.")]
    public static void InvokeAllStaticMethods(
        this Assembly assembly,
        string typeName,
        string methodName,
        params object?[]? parameters
    )
    {
        var methods = assembly
            .GetExportedTypes()
            .Where(type => type.IsPublic && string.Equals(type.Name, typeName, StringComparison.Ordinal))
            .Select(type => type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static));

        foreach (var method in methods)
        {
            method?.Invoke(obj: null, parameters);
        }
    }

    #endregion
}
