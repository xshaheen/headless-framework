// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Headless.Security;

internal static partial class SecretHasherLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SecretHasherCostOutOfRange",
        Level = LogLevel.Warning,
        Message = "{Problem} Set SecretHasherOptions.CostCheck.Mode to Strict to fail startup instead, or Off to skip the check."
    )]
    public static partial void LogSecretHasherCostOutOfRange(this ILogger logger, string problem);

    [LoggerMessage(
        EventId = 2,
        EventName = "SecretHasherCostCheckFailed",
        Level = LogLevel.Warning,
        Message = "The secret-hasher cost check could not hash with '{AlgorithmId}'; its cost was not measured."
    )]
    public static partial void LogSecretHasherCostCheckFailed(
        this ILogger logger,
        Exception exception,
        string algorithmId
    );
}
