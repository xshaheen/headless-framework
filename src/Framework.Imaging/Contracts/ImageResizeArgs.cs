namespace Framework.Imaging.Contracts;

public sealed class ImageResizeArgs
{
    private int _width;
    private int _height;

    public int Width
    {
        get => _width;
        set
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
        set
        {
            if (value < 0)
            {
                throw new ArgumentException("Height cannot be negative!", nameof(value));
            }

            _height = value;
        }
    }

    public ImageResizeMode Mode { get; set; } = ImageResizeMode.Default;

    public ImageResizeArgs(int? width = null, int? height = null, ImageResizeMode? mode = null)
    {
        if (mode.HasValue)
        {
            Mode = mode.Value;
        }

        Width = width ?? 0;
        Height = height ?? 0;
    }
}
