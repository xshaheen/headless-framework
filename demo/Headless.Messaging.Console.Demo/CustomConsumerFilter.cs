using Headless.Messaging;

namespace Demo;

public sealed class CustomConsumerFilter : ConsumeFilter
{
    public override ValueTask OnSubscribeExceptionAsync(
        ExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        return context.Exception.InnerException is TimeoutException
            ? throw new TimeoutException("Http request timeout")
            : base.OnSubscribeExceptionAsync(context, cancellationToken);
    }
}
