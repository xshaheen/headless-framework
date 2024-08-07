namespace Framework.Imaging.Contracts;

public sealed class ImageCompressResult<T>(T result, ImageProcessState state) : ImageProcessResult<T>(result, state);
