// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Logging;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Known personal data and secrets the suite pushes through each pipeline. Every scenario gets its own values, so a
/// value found in an export can only have come from the scenario that wrote it.
/// </summary>
internal static class Pii
{
    /// <summary>The shape the "redaction on" configuration masks as free text: a 14-digit national ID.</summary>
    public const string NationalIdPattern = @"\b\d{14}\b";

    /// <summary>A 14-digit national ID unique to <paramref name="scenario"/>.</summary>
    public static string NationalId(int scenario) => "2900101" + scenario.ToString("D7", CultureInfo.InvariantCulture);

    /// <summary>An e-mail address unique to <paramref name="scenario"/>.</summary>
    public static string Email(string scenario) => $"jane.{scenario}@leak.example";

    /// <summary>An API key with no recognizable shape, so only classification can keep it out.</summary>
    public static string Token(string scenario) => $"sk_live_{scenario}Z9q7Xw2Lp";

    /// <summary>A non-sensitive literal that proves the entry reached the sink at all.</summary>
    public static string Marker(string scenario) => $"marker-{scenario}";
}

/// <summary>A record with one classified member, logged destructured and through <c>[LogProperties]</c>.</summary>
public sealed record LeakCustomer([property: PrivateData] string Email, string Tier);

internal static partial class LeakLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "marker-m3 signup {Email}")]
    public static partial void ClassifiedParameter(ILogger logger, [PrivateData] string email);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "marker-m12 customer")]
    public static partial void LogProperties(ILogger logger, [LogProperties] LeakCustomer customer);
}
