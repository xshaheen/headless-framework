// Copyright (c) Mahmoud Shaheen. All rights reserved.

using DotNetCore.CAP.Filter;

namespace Framework.Messaging.Filters;

/// <summary>
/// By default, if the consumer throws an OperationCanceledException (including TaskCanceledException),
/// we consider this to be normal user behavior and ignore the exception.
/// If you use HTTPClient in the consumer method and configure the request timeout, due to the design issue of HTTP Client
/// (refer to https://github.com/dotnet/runtime/issues/21965 and this for solutions https://stackoverflow.com/a/65989456),
/// you may need to handle the exception separately and re-throw non OperationCanceledException.
/// </summary>
[PublicAPI]
public sealed class StopMarkHttpTimeoutAsSuccessFilter : SubscribeFilter
{
    public override Task OnSubscribeExceptionAsync(ExceptionContext context)
    {
        // https://github.com/dotnetcore/cap/issues/1368
        if (context.Exception.InnerException is TimeoutException)
        {
            throw context.Exception.InnerException;
        }

        return base.OnSubscribeExceptionAsync(context);
    }
}
