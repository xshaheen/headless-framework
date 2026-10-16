// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.EntityFrameworkCore.ChangeTracking;
using File = Headless.Primitives.File;
using Image = Headless.Primitives.Image;

namespace Headless.EntityFramework;

/// <summary>
/// Structural comparer for a <see cref="File"/> column stored as JSON. Equality covers the members the JSON
/// converter stores for the declared type, so it answers the same question as comparing the stored JSON, without
/// serializing on every change-detection pass.
/// </summary>
internal sealed class FileValueComparer()
    : ValueComparer<File?>(
        (left, right) => HeadlessMediaComparison.AreEqual(left, right),
        value => HeadlessMediaComparison.GetHashCode(value),
        value => HeadlessMediaComparison.SnapshotFile(value)
    );

/// <summary>Structural comparer for an <see cref="Image"/> column stored as JSON; see <see cref="FileValueComparer"/>.</summary>
internal sealed class ImageValueComparer()
    : ValueComparer<Image?>(
        (left, right) => HeadlessMediaComparison.AreEqual(left, right),
        value => HeadlessMediaComparison.GetHashCode(value),
        value => HeadlessMediaComparison.SnapshotImage(value)
    );

internal static class HeadlessMediaComparison
{
    public static bool AreEqual(File? left, File? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal)
            && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
            && string.Equals(left.SavedName, right.SavedName, StringComparison.Ordinal)
            && string.Equals(left.Url, right.Url, StringComparison.Ordinal)
            && string.Equals(left.ContentType, right.ContentType, StringComparison.Ordinal)
            // EqualsExact, because the stored JSON keeps the offset and Equals compares only the instant.
            && left.UploadedAt.EqualsExact(right.UploadedAt)
            && left.Length == right.Length
            && left.Private == right.Private
            && string.Equals(left.Md5, right.Md5, StringComparison.Ordinal)
            && _AreEqual(left.Container, right.Container)
            && HeadlessJsonValueEquality.AreEqual(left.Metadata, right.Metadata);
    }

    public static bool AreEqual(Image? left, Image? right)
    {
        return AreEqual((File?)left, right)
            && (
                left is null
                || (
                    string.Equals(left.Caption, right!.Caption, StringComparison.Ordinal)
                    && left.Width == right.Width
                    && left.Height == right.Height
                )
            );
    }

    public static int GetHashCode(File? value)
    {
        return value is null
            ? 0
            : HashCode.Combine(StringComparer.Ordinal.GetHashCode(value.Id), value.Length, value.UploadedAt);
    }

    // The JSON comparer's snapshot was a round trip through the declared type, so a File snapshot is a File even when
    // the property holds a derived instance: only File members are stored for a File column.
    public static File? SnapshotFile(File? value)
    {
        return value is null
            ? null
            : new File
            {
                Id = value.Id,
                DisplayName = value.DisplayName,
                SavedName = value.SavedName,
                Url = value.Url,
                Container = _Copy(value.Container),
                ContentType = value.ContentType,
                UploadedAt = value.UploadedAt,
                Length = value.Length,
                Private = value.Private,
                Md5 = value.Md5,
                Metadata = _Copy(value.Metadata),
            };
    }

    public static Image? SnapshotImage(Image? value)
    {
        return value is null
            ? null
            : new Image
            {
                Id = value.Id,
                DisplayName = value.DisplayName,
                SavedName = value.SavedName,
                Url = value.Url,
                Container = _Copy(value.Container),
                ContentType = value.ContentType,
                UploadedAt = value.UploadedAt,
                Length = value.Length,
                Private = value.Private,
                Md5 = value.Md5,
                Metadata = _Copy(value.Metadata),
                Caption = value.Caption,
                Width = value.Width,
                Height = value.Height,
            };
    }

    private static bool _AreEqual(string[]? left, string[]? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        // String equality is ordinal, which is what the stored JSON compares.
        return left is not null && right is not null && left.AsSpan().SequenceEqual(right);
    }

    private static string[] _Copy(string[]? values) => values is null ? null! : [.. values];

    private static Dictionary<string, object?>? _Copy(Dictionary<string, object?>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        var copy = new Dictionary<string, object?>(metadata.Count, metadata.Comparer);

        foreach (var (key, value) in metadata)
        {
            copy[key] = HeadlessJsonValueEquality.Snapshot(value);
        }

        return copy;
    }
}
