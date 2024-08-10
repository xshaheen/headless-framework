namespace Framework.Imaging.Contracts;

public sealed class ImagingOptions
{
    public ImageResizeMode DefaultResizeMode { get; set; } = ImageResizeMode.None;
}
