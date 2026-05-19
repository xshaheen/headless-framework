// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Demo;

public sealed class CustomConsumerMiddleware : IConsumeMiddleware<ConsumeContext>
{
    public async ValueTask InvokeAsync(ConsumeContext context, Func<ValueTask> next)
    {
        try
        {
            await next();
        }
        catch (Exception ex) when (ex.InnerException is TimeoutException)
        {
            throw new TimeoutException("Http request timeout", ex);
        }
    }
}
