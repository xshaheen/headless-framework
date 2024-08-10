namespace Framework.Imaging.Contracts;

public sealed class ImageCompressArgs(string? mimeType = null)
{
    public string? MimeType { get; private init; } = mimeType;
}
