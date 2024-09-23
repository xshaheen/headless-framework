// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DiagnosticAdapter;
using Microsoft.Extensions.Logging;

namespace Framework.Api.Core.Diagnostics;

/// <summary>see: https://docs.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/diagnostics</summary>
[PublicAPI]
public sealed partial class BadRequestDiagnosticAdapter(ILogger logger)
{
    [DiagnosticName(DiagnosticSources.KestrelOnBadRequest)]
    public void OnBadRequest(KeyValuePair<string, object?> value)
    {
        if (value.Value is not IFeatureCollection featureCollection)
        {
            return;
        }

        var badRequestFeature = featureCollection.Get<IBadRequestExceptionFeature>();

        if (badRequestFeature is not null)
        {
            Extensions.BadRequestEvent(logger, badRequestFeature.Error);
        }
    }

    private static partial class Extensions
    {
        [LoggerMessage(
            EventId = 5104,
            EventName = "BadRequestEvent",
            Level = LogLevel.Warning,
            SkipEnabledCheck = true,
            Message = "Bad request received"
        )]
        public static partial void BadRequestEvent(ILogger logger, Exception? exception);
    }
}
