// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;

namespace Framework.Blobs.Internals;

/// <summary>
/// Shared path validation for blob storage providers.
/// </summary>
public static class PathValidation
{
    public static void ThrowIfPathTraversal(
        string? path,
        [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        // Reject path traversal patterns:
        // - ../  (unix-style)
        // - ..\  (windows-style)
        // - /..  (traversal at end)
        // - \..  (traversal at end)
        // - path ending with ..
        // - path starting with ..
        if (path.Contains("../", StringComparison.Ordinal) ||
            path.Contains("..\\", StringComparison.Ordinal) ||
            path.Contains("/..", StringComparison.Ordinal) ||
            path.Contains("\\..", StringComparison.Ordinal) ||
            path.StartsWith("..", StringComparison.Ordinal) ||
            path.EndsWith("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Path traversal sequences are not allowed", paramName);
        }
    }

    public static void ThrowIfAbsolutePath(
        string? path,
        [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        if (path.StartsWith('/') || path.StartsWith('\\'))
        {
            throw new ArgumentException("Absolute paths are not allowed", paramName);
        }
    }

    public static void ThrowIfControlCharacters(
        string? path,
        [CallerArgumentExpression(nameof(path))] string? paramName = null)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        foreach (var c in path)
        {
            if (c < 32)
            {
                throw new ArgumentException("Control characters are not allowed in path", paramName);
            }
        }
    }

    /// <summary>
    /// Validates a path segment for security issues including path traversal,
    /// absolute paths, and control characters.
    /// </summary>
    public static void ValidatePathSegment(
        string? segment,
        [CallerArgumentExpression(nameof(segment))] string? paramName = null)
    {
        ThrowIfPathTraversal(segment, paramName);
        ThrowIfAbsolutePath(segment, paramName);
        ThrowIfControlCharacters(segment, paramName);
    }
}
