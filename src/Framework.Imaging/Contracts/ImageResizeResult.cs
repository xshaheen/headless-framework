using Framework.Arguments;

namespace Framework.Imaging.Contracts;

public sealed class ImageStreamResizeResult : ImageProcessResult<ImageResizeContent<Stream>>
{
    private ImageStreamResizeResult() { }

    public static ImageStreamResizeResult CannotRead() => NotSupported(CannotReadError);

    public static ImageStreamResizeResult NotSupportedMimeType(string mimType)
    {
        return NotSupported($"The given MIME type {mimType} is not supported.");
    }

    public static ImageStreamResizeResult NotSupported(string error = UnsupportedError)
    {
        return new() { State = ImageProcessState.Unsupported, Error = Argument.IsNotNull(error) };
    }

    public static ImageStreamResizeResult Failed(string error = FailedError)
    {
        return new() { State = ImageProcessState.Failed, Error = Argument.IsNotNull(error) };
    }

    public static ImageStreamResizeResult Done(Stream content, string mimeType, int width, int height)
    {
        return new()
        {
            State = ImageProcessState.Done,
            Result = new()
            {
                Content = Argument.IsNotNull(content),
                MimeType = Argument.IsNotNull(mimeType),
                Width = Argument.IsPositive(width),
                Height = Argument.IsPositive(height)
            }
        };
    }
}

public sealed class ImageResizeContent<TContent>
{
    public required TContent Content { get; init; }

    public required string MimeType { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }
}
