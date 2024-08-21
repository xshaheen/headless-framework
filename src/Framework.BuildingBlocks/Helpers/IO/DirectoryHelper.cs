using System.Buffers;

namespace Framework.BuildingBlocks.Helpers.IO;

/// <summary>A helper class for Directory operations.</summary>
[PublicAPI]
public static class DirectoryHelper
{
    #region Invalid DirectoryName Characters

    public static readonly SearchValues<char> InvalidDirectoryNameChars = SearchValues.Create(
        [
            '|',
            '\0',
            '\u0001',
            '\u0002',
            '\u0003',
            '\u0004',
            '\u0005',
            '\u0006',
            '\a',
            '\b',
            '\t',
            '\n',
            '\v',
            '\f',
            '\r',
            '\u000E',
            '\u000F',
            '\u0010',
            '\u0011',
            '\u0012',
            '\u0013',
            '\u0014',
            '\u0015',
            '\u0016',
            '\u0017',
            '\u0018',
            '\u0019',
            '\u001A',
            '\u001B',
            '\u001C',
            '\u001D',
            '\u001E',
            '\u001F',
        ]
    );

    #endregion

    #region Create If Not Exists

    public static void CreateIfNotExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public static void CreateIfNotExists(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            directory.Create();
        }
    }

    #endregion

    #region Delete If Exists

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

    #endregion

    #region Is Subdirectory Of

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

    #endregion

    #region Is Valid Directory Name

    public static bool IsValidDirectoryName(string directoryName)
    {
        Argument.IsNotNull(directoryName);

        return directoryName.All(c => !InvalidDirectoryNameChars.Contains(c));
    }

    #endregion
}
