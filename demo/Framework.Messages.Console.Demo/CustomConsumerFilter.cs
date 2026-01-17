using Framework.Messages;

namespace Demo;

public sealed class CustomConsumerFilter : ConsumeFilter
{
    public override ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
    {
        return context.Exception.InnerException is TimeoutException
            ? throw new TimeoutException("Http request timeout")
            : base.OnSubscribeExceptionAsync(context);
    }
}
