// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;
using Microsoft.Extensions.Compliance.Redaction;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Logging.Internal;

/// <summary>
/// Warns once at startup when log redaction is registered but the logger factory in charge is not wrapped, because a
/// factory registered later (for example Serilog's <c>UseSerilog</c>) replaced the wrapped one.
/// </summary>
/// <remarks>
/// It reads one registration and writes nothing. It warns instead of failing the start: the host can still serve
/// requests, and the warning names the fix.
/// </remarks>
internal sealed class LogRedactionStartupValidator(IServiceProvider services, ILoggerFactory loggerFactory)
    : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        var hasRules =
            services.GetService<IRedactorProvider>() is not null
            || services.GetServices<ILogPathRedactor>().Any()
            || services.GetServices<ILogTextRedactor>().Any();

        if (hasRules && loggerFactory is not RedactingLoggerFactory)
        {
            loggerFactory
                .CreateLogger<LogRedactionStartupValidator>()
                .LogFactoryNotRedacting(loggerFactory.GetType().FullName ?? loggerFactory.GetType().Name);
        }

        return Task.CompletedTask;
    }
}

internal static partial class LogRedactionLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "LogRedactionFactoryReplaced",
        Level = LogLevel.Warning,
        Message = "Log redaction is registered, but the logger factory in charge ({LoggerFactoryType}) does not apply it: a logger factory registered after AddHeadlessLogRedaction replaced the redacting one. Classified [LoggerMessage] parameters and credential paths in ASP.NET Core's request logs are not redacted. Call AddHeadlessLogRedaction after the logger factory is registered, for example after UseSerilog."
    )]
    internal static partial void LogFactoryNotRedacting(this ILogger logger, string loggerFactoryType);
}
