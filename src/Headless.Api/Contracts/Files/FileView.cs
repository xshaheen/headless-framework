// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using File = Headless.Primitives.File;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Api.Contracts;

public class FileView
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required string Url { get; init; }

    public required long Length { get; init; }

    public required string ContentType { get; init; }

    public required DateTimeOffset DateUploaded { get; init; }

    [return: NotNullIfNotNull(nameof(operand))]
    public static FileView? FromFile(File? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator FileView?(File? operand)
    {
        if (operand is null)
        {
            return null;
        }

        return new()
        {
            Id = operand.Id,
            FileName = operand.DisplayName,
            Url = operand.Url,
            Length = operand.Length,
            ContentType = operand.ContentType,
            DateUploaded = operand.DateUploaded,
        };
    }
}
