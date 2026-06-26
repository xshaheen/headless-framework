// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using Headless.Checks;

namespace Headless.IO;

/// <summary>A helper class for Directory operations.</summary>
[PublicAPI]
public static class DirectoryHelper
{
    #region Invalid DirectoryName Characters

    /// <summary>
    /// The set of characters that are not allowed in a directory name: the pipe character (<c>|</c>) and all
    /// control characters in the range <c>U+0000</c> through <c>U+001F</c>. Used by <see cref="IsValidDirectoryName"/>.
    /// </summary>
    public static readonly SearchValues<char> InvalidDirectoryNameChars = SearchValues.Create(
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
        '\u001F'
    );

    #endregion

    #region Create If Not Exists

    /// <summary>Creates the directory at <paramref name="directory"/> if it does not already exist.</summary>
    /// <param name="directory">The path of the directory to create.</param>
    /// <exception cref="IOException">Thrown when the directory cannot be created (for example a file with the same name exists).</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have the required permission.</exception>
    public static void CreateIfNotExists(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>Creates the directory described by <paramref name="directory"/> if it does not already exist.</summary>
    /// <param name="directory">The directory to create.</param>
    /// <exception cref="IOException">Thrown when the directory cannot be created (for example a file with the same name exists).</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have the required permission.</exception>
    public static void CreateIfNotExists(DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            directory.Create();
        }
    }

    #endregion

    #region Delete If Exists

    /// <summary>Deletes the empty directory at <paramref name="directory"/> if it exists.</summary>
    /// <param name="directory">The path of the directory to delete.</param>
    /// <exception cref="IOException">Thrown when the directory is not empty, is in use, or is read-only.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have the required permission.</exception>
    public static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory);
        }
    }

    /// <summary>Deletes the directory at <paramref name="directory"/> if it exists.</summary>
    /// <param name="directory">The path of the directory to delete.</param>
    /// <param name="recursive">
    /// <see langword="true"/> to delete the directory, its subdirectories, and all files; otherwise <see langword="false"/>.
    /// </param>
    /// <exception cref="IOException">Thrown when the directory is in use, is read-only, or (when <paramref name="recursive"/> is <see langword="false"/>) is not empty.</exception>
    /// <exception cref="UnauthorizedAccessException">Thrown when the caller does not have the required permission.</exception>
    public static void DeleteIfExists(string directory, bool recursive)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive);
        }
    }

    #endregion

    #region Is Subdirectory Of

    /// <summary>
    /// Determines whether <paramref name="childDirectoryPath"/> is the same as, or a descendant of,
    /// <paramref name="parentDirectoryPath"/>.
    /// </summary>
    /// <param name="parentDirectoryPath">The path of the candidate parent (ancestor) directory.</param>
    /// <param name="childDirectoryPath">The path of the candidate child (descendant) directory.</param>
    /// <returns>
    /// <see langword="true"/> if the two paths are equal or <paramref name="childDirectoryPath"/> is nested under
    /// <paramref name="parentDirectoryPath"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parentDirectoryPath"/> or <paramref name="childDirectoryPath"/> is <see langword="null"/>.</exception>
    public static bool IsSubDirectoryOf(string parentDirectoryPath, string childDirectoryPath)
    {
        Argument.IsNotNull(parentDirectoryPath);
        Argument.IsNotNull(childDirectoryPath);

        return IsSubDirectoryOf(new DirectoryInfo(parentDirectoryPath), new DirectoryInfo(childDirectoryPath));
    }

    /// <summary>
    /// Determines whether <paramref name="childDirectory"/> is the same as, or a descendant of,
    /// <paramref name="parentDirectory"/> by comparing their full paths.
    /// </summary>
    /// <param name="parentDirectory">The candidate parent (ancestor) directory.</param>
    /// <param name="childDirectory">The candidate child (descendant) directory.</param>
    /// <returns>
    /// <see langword="true"/> if the two directories have the same full path or <paramref name="childDirectory"/> is
    /// nested under <paramref name="parentDirectory"/>; otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="parentDirectory"/> or <paramref name="childDirectory"/> is <see langword="null"/>.</exception>
    public static bool IsSubDirectoryOf(DirectoryInfo parentDirectory, DirectoryInfo childDirectory)
    {
        Argument.IsNotNull(parentDirectory);
        Argument.IsNotNull(childDirectory);

        // File-name case sensitivity is platform-dependent: Windows (NTFS) and macOS (APFS, default) match
        // paths case-insensitively, while Linux is case-sensitive. Compare the same way the host filesystem
        // does so a differently-cased ancestor is still recognized on case-insensitive platforms.
        var comparison =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (string.Equals(parentDirectory.FullName, childDirectory.FullName, comparison))
        {
            return true;
        }

        var parentOfChild = childDirectory.Parent;

        return parentOfChild is not null && IsSubDirectoryOf(parentDirectory, parentOfChild);
    }

    #endregion

    #region Is Valid Directory Name

    /// <summary>
    /// Determines whether <paramref name="directoryName"/> contains only characters allowed in a directory name,
    /// that is, none of the characters in <see cref="InvalidDirectoryNameChars"/>.
    /// </summary>
    /// <param name="directoryName">The directory name to validate.</param>
    /// <returns><see langword="true"/> if every character is allowed; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directoryName"/> is <see langword="null"/>.</exception>
    public static bool IsValidDirectoryName(string directoryName)
    {
        Argument.IsNotNull(directoryName);

        // Vectorized scan over the whole span; the LINQ All(...Contains) form defeats SearchValues.
        return !directoryName.AsSpan().ContainsAny(InvalidDirectoryNameChars);
    }

    #endregion
}
