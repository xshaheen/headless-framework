// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;

namespace Framework.Imaging.Contracts;

public sealed class ImageStreamCompressResult : ImageProcessResult<Stream>
{
    private ImageStreamCompressResult() { }

    public static ImageStreamCompressResult CannotRead() => NotSupported(CannotReadError);

    public static ImageStreamCompressResult NotSupported(string error = UnsupportedError)
    {
        return new() { State = ImageProcessState.Unsupported, Error = Argument.IsNotNull(error) };
    }

    public static ImageStreamCompressResult NotSupportedMimeType(string mimType)
    {
        return NotSupported($"The given MIME type {mimType} is not supported.");
    }

    public static ImageStreamCompressResult Failed(string error = FailedError)
    {
        return new() { State = ImageProcessState.Failed, Error = Argument.IsNotNull(error) };
    }

    public static ImageStreamCompressResult Done(Stream content)
    {
        return new() { State = ImageProcessState.Done, Result = Argument.IsNotNull(content) };
    }
}
