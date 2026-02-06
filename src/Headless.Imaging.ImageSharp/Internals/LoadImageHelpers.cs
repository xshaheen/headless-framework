// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;

namespace Headless.Imaging.ImageSharp.Internals;

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
            logger.LogImageStreamNotReadable(e);

            return (null, _NotReadableError);
        }
        catch (InvalidImageContentException e)
        {
            logger.LogEncodedImageInvalidContent(e);

            return (null, _InvalidContentError);
        }
        catch (UnknownImageFormatException e)
        {
            logger.LogEncodedImageUnknownFormat(e);

            return (null, _UnknownFormatError);
        }

        return (image, null);
    }
}

internal static partial class LoadImageHelpersLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "ImageStreamNotReadable",
        Level = LogLevel.Information,
        Message = "The stream is not readable or the image format is not supported"
    )]
    public static partial void LogImageStreamNotReadable(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "EncodedImageInvalidContent",
        Level = LogLevel.Information,
        Message = "The encoded image contains invalid content"
    )]
    public static partial void LogEncodedImageInvalidContent(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "EncodedImageUnknownFormat",
        Level = LogLevel.Information,
        Message = "The encoded image format is unknown"
    )]
    public static partial void LogEncodedImageUnknownFormat(this ILogger logger, Exception exception);
}
