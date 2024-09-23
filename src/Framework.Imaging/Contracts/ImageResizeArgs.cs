// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Imaging.Contracts;

public sealed class ImageResizeArgs
{
    private readonly int _width;
    private readonly int _height;

    public int Width
    {
        get => _width;
        private init
        {
            if (value < 0)
            {
                throw new ArgumentException("Width cannot be negative!", nameof(value));
            }

            _width = value;
        }
    }

    public int Height
    {
        get => _height;
        private init
        {
            if (value < 0)
            {
                throw new ArgumentException("Height cannot be negative!", nameof(value));
            }

            _height = value;
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

    public ImageResizeArgs(int? width = null, int? height = null, string? mimeType = null, ImageResizeMode? mode = null)
    {
        if (mode.HasValue)
        {
            Mode = mode.Value;
        }

        Width = width ?? 0;
        Height = height ?? 0;
        MimeType = mimeType;
    }
}
