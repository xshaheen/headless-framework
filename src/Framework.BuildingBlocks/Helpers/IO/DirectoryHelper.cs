namespace Framework.BuildingBlocks.Helpers.IO;

/// <summary>A helper class for Directory operations.</summary>
[PublicAPI]
public static class DirectoryHelper
{
    public static void CreateIfNotExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }
    }

    public static void DeleteIfExists(string directory, bool recursive)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive);
        }
    }

    public static void CreateIfNotExists(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            directory.Create();
        }
    }

    public static bool IsSubDirectoryOf(string parentDirectoryPath, string childDirectoryPath)
    {
        Argument.IsNotNull(parentDirectoryPath);
        Argument.IsNotNull(childDirectoryPath);

        return IsSubDirectoryOf(new DirectoryInfo(parentDirectoryPath), new DirectoryInfo(childDirectoryPath));
    }

    public static bool IsSubDirectoryOf(DirectoryInfo parentDirectory, DirectoryInfo childDirectory)
    {
        Argument.IsNotNull(parentDirectory);
        Argument.IsNotNull(childDirectory);

        if (string.Equals(parentDirectory.FullName, childDirectory.FullName, StringComparison.Ordinal))
        {
            return true;
        }

        var parentOfChild = childDirectory.Parent;

        return parentOfChild is not null && IsSubDirectoryOf(parentDirectory, parentOfChild);
    }
}
