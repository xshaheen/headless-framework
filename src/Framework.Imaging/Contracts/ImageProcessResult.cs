namespace Framework.Imaging.Contracts;

public abstract class ImageProcessResult<T>(T result, ImageProcessState state)
{
    public T Result { get; } = result;

    public ImageProcessState State { get; } = state;
}
