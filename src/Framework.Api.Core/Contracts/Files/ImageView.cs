// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using System.Diagnostics.CodeAnalysis;
using Framework.Kernel.Primitives;

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Api.Core.Contracts;

public sealed class ImageView : FileView
{
    public string? Caption { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    [return: NotNullIfNotNull(nameof(operand))]
    public static ImageView? FromImage(Image? operand) => operand;

    [return: NotNullIfNotNull(nameof(operand))]
    public static implicit operator ImageView?(Image? operand)
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
            Caption = operand.Caption,
            Width = operand.Width,
            Height = operand.Height,
        };
    }
}
