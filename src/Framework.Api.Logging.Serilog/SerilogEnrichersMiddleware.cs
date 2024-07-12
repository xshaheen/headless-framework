using Framework.Api.Core.Abstractions;
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace Framework.Api.Logging.Serilog;

public sealed class SerilogEnrichersMiddleware(IRequestContext requestContext) : IMiddleware
{
    private const string _UserId = "UserId";
    private const string _AccountId = "AccountId";
    private const string _TenantId = "TenantId";
    private const string _CorrelationId = "CorrelationId";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var enrichers = new List<ILogEventEnricher>();

        if (requestContext.UserId is not null)
        {
            enrichers.Add(new PropertyEnricher(_UserId, requestContext.UserId));
        }

        if (requestContext.AccountId is not null)
        {
            enrichers.Add(new PropertyEnricher(_AccountId, requestContext.AccountId));
        }

        if (requestContext.TenantId is not null)
        {
            enrichers.Add(new PropertyEnricher(_TenantId, requestContext.TenantId));
        }

        if (requestContext.CorrelationId is not null)
        {
            enrichers.Add(new PropertyEnricher(_CorrelationId, requestContext.CorrelationId));
        }

        using (LogContext.Push([.. enrichers]))
        {
            await next(context);
        }
    }
}
