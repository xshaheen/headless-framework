using System.Reflection;
using System.Runtime.Loader;

namespace Framework.BuildingBlocks.Helpers;

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

    public static IReadOnlyList<Type?> GetAllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }

    /// <summary>
    /// Gets the informational version of an assembly.
    /// </summary>
    /// <param name="assembly">The assembly. May not be null.</param>
    /// <returns>The version represented as a string. May not be null.</returns>
    public static string? GetInformationalVersion(this Assembly assembly)
    {
        Argument.IsNotNull(assembly);

        var attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        return attr?.InformationalVersion;
    }
}
