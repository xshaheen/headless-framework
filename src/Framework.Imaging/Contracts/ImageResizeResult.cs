namespace Framework.Imaging.Contracts;

public sealed class ImageResizeResult<T>(T result, ImageProcessState state) : ImageProcessResult<T>(result, state);
