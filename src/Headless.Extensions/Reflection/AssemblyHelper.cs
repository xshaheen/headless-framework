// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.Loader;
using MoreLinq;

namespace Headless.Reflection;

/// <summary>
/// Helpers for discovering, loading, scanning, and invoking members on assemblies.
/// </summary>
[PublicAPI]
public static class AssemblyHelper
{
    /// <summary>
    /// Determines whether an assembly full name belongs to the <c>System.*</c> or <c>Microsoft.*</c> namespaces.
    /// </summary>
    /// <param name="assemblyFullName">The assembly full name to inspect. May be <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="assemblyFullName"/> is non-<see langword="null"/> and starts with
    /// <c>System.</c> or <c>Microsoft.</c>; otherwise <see langword="false"/>.
    /// </returns>
    public static bool IsSystemAssemblyName(string? assemblyFullName)
    {
        return assemblyFullName is not null
            && (
                assemblyFullName.StartsWith("System.", StringComparison.Ordinal)
                || assemblyFullName.StartsWith("Microsoft.", StringComparison.Ordinal)
            );
    }

    #region Get Assemblies From Folder

    /// <summary>
    /// Loads every <c>.dll</c> and <c>.exe</c> file found under the given folder into the default load context.
    /// </summary>
    /// <param name="folderPath">The folder to search for assembly files.</param>
    /// <param name="searchOption">Whether to search only the top directory or recurse into subdirectories.</param>
    /// <returns>The list of loaded assemblies.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folderPath"/> is an invalid path (for example contains invalid characters).</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="folderPath"/> does not exist.</exception>
    /// <exception cref="BadImageFormatException">Thrown when a discovered file is not a valid managed assembly.</exception>
    /// <exception cref="FileLoadException">Thrown when a discovered assembly file is found but cannot be loaded.</exception>
    [RequiresUnreferencedCode("Loading assemblies from path might load types that cannot be statically analyzed.")]
    public static List<Assembly> LoadAssemblies(string folderPath, SearchOption searchOption)
    {
        return [.. GetAssemblyFiles(folderPath, searchOption).Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)];
    }

    /// <summary>
    /// Enumerates the <c>.dll</c> and <c>.exe</c> file paths found under the given folder.
    /// </summary>
    /// <param name="folderPath">The folder to search.</param>
    /// <param name="searchOption">Whether to search only the top directory or recurse into subdirectories.</param>
    /// <returns>The matching assembly file paths.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folderPath"/> is an invalid path (for example contains invalid characters).</exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folderPath"/> is <see langword="null"/>.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="folderPath"/> does not exist.</exception>
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

    /// <summary>
    /// Scans the assemblies currently loaded into <see cref="AppDomain.CurrentDomain"/> and breadth-first walks their
    /// referenced assemblies (loading references on demand), returning the assemblies accepted by
    /// <paramref name="acceptPredicate"/>. References rejected by <paramref name="excludePredicate"/> are not traversed,
    /// and references that fail to load are skipped.
    /// </summary>
    /// <param name="acceptPredicate">Predicate selecting which assemblies to include in the result.</param>
    /// <param name="excludePredicate">Predicate, evaluated against an assembly full name, selecting which assemblies (and their references) to skip.</param>
    /// <returns>The set of accepted assemblies.</returns>
    [RequiresUnreferencedCode("Assembly scanning is not compatible with trimming.")]
    public static HashSet<Assembly> GetCurrentAssemblies(
        Func<Assembly, bool> acceptPredicate,
        Func<string, bool> excludePredicate
    )
    {
        var currentlyLoadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        var (excluded, included) = currentlyLoadedAssemblies.Partition(assembly =>
            assembly.FullName is null || excludePredicate(assembly.FullName)
        );

        // Put all the exclude assemblies as checked (dynamic assemblies with null FullName are already excluded above)
        HashSet<string> referencesCheckedNames = new(
            excluded.Select(a => a.FullName).Where(n => n is not null)!,
            StringComparer.Ordinal
        );
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

                // Mark as visited before attempting to load so a load failure doesn't cause
                // repeated retry attempts in subsequent BFS iterations.
                referencesCheckedNames.Add(reference.FullName);

                Assembly loadedAssembly;

                try
                {
                    loadedAssembly = Assembly.Load(reference);
                }
                catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or FileLoadException)
                {
                    // Reference is unavailable or invalid — skip and continue the BFS.
                    continue;
                }

                assembliesToCheck.Enqueue(loadedAssembly);
            }
        }

        return acceptedAssemblies;
    }

    #endregion

    #region Invoke Static Methods

    /// <summary>
    /// For each assembly, finds public types whose name equals <paramref name="typeName"/> and invokes their public
    /// static method named <paramref name="methodName"/> with the supplied <paramref name="parameters"/>.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <param name="typeName">The exact (case-sensitive) name of the type whose static method is invoked.</param>
    /// <param name="methodName">The name of the public static method to invoke. Missing methods are skipped.</param>
    /// <param name="parameters">The arguments passed to each invoked method.</param>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when an invoked method throws; the original exception is available via the inner exception.</exception>
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

    /// <summary>
    /// Finds public types in the assembly whose name equals <paramref name="typeName"/> and invokes their public
    /// static method named <paramref name="methodName"/> with the supplied <paramref name="parameters"/>.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <param name="typeName">The exact (case-sensitive) name of the type whose static method is invoked.</param>
    /// <param name="methodName">The name of the public static method to invoke. Missing methods are skipped.</param>
    /// <param name="parameters">The arguments passed to each invoked method.</param>
    /// <exception cref="System.Reflection.TargetInvocationException">Thrown when an invoked method throws; the original exception is available via the inner exception.</exception>
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
