// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Imaging;

public sealed class ImageResizeArgs
{
    public int Width
    {
        get;
        private init
        {
            if (value < 0)
            {
                throw new ArgumentException("Width cannot be negative!", nameof(value));
            }

            field = value;
        }
    }

    public int Height
    {
        get;
        private init
        {
            if (value < 0)
            {
                throw new ArgumentException("Height cannot be negative!", nameof(value));
            }

            field = value;
        }
    }

    public string? MimeType { get; private init; }

    public ImageResizeMode Mode { get; set; } = ImageResizeMode.Default;

    public void ChangeDefaultResizeMode(ImageResizeMode defaultMode)
    {
        if (Mode is ImageResizeMode.Default)
        {
            Mode = defaultMode;
        }
    }

    public ImageResizeArgs(string mimeType)
    {
        MimeType = Argument.IsNotNullOrWhiteSpace(mimeType);
    }

    public ImageResizeArgs(ImageResizeMode mode, int width, int? height = null, string? mimeType = null)
    {
        Argument.IsPositive(height);

        Mode = Argument.IsInEnum(mode);
        Width = Argument.IsPositive(width);
        Height = height ?? 0;
        MimeType = mimeType;
    }

    public ImageResizeArgs(ImageResizeMode mode, int? width, int height, string? mimeType = null)
    {
        Argument.IsPositive(width);

        Mode = Argument.IsInEnum(mode);
        Width = width ?? 0;
        Height = Argument.IsPositive(height);
        MimeType = mimeType;
    }

    public ImageResizeArgs(ImageResizeMode mode, int width, int height, string? mimeType = null)
    {
        Mode = Argument.IsInEnum(mode);
        Width = Argument.IsPositive(width);
        Height = Argument.IsPositive(height);
        MimeType = mimeType;
    }
}
