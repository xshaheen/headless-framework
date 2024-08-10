using Framework.Imaging.Contracts;

namespace Framework.Imaging;

public interface IImageResizerContributor
{
    Task<ImageStreamResizeResult> TryResizeAsync(
        Stream stream,
        ImageResizeArgs args,
        CancellationToken cancellationToken = default
    );
}
