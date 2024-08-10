using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Framework.Imaging.ImageSharp.Internals;

internal static class LoadImageHelpers
{
    private const string _NotReadableError = "The stream is not readable or the image format is not supported.";
    private const string _InvalidContentError = "The encoded image contains invalid content.";
    private const string _UnknownFormatError = "The encoded image format is unknown.";

    public static async Task<(Image? image, string? Error)> TryLoad(
        Stream stream,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        Image image;

        try
        {
            image = await Image.LoadAsync(stream, cancellationToken);
        }
        catch (NotSupportedException e)
        {
            logger.LogInformation(e, "The stream is not readable or the image format is not supported");

            return (null, _NotReadableError);
        }
        catch (InvalidImageContentException e)
        {
            logger.LogInformation(e, "The encoded image contains invalid content");

            return (null, _InvalidContentError);
        }
        catch (UnknownImageFormatException e)
        {
            logger.LogInformation(e, "The encoded image format is unknown");

            return (null, _UnknownFormatError);
        }

        return (image, null);
    }
}
