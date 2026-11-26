// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.

namespace Headless.RateLimiting;

/// <summary>One declared purpose and its quota.</summary>
internal sealed record RateLimitingPurposeView(string Name, int Limit, long WindowSeconds);

/// <summary>
/// The purposes the app declares, sorted by name, the one with the shortest window, and where the built-in limiter
/// keeps its counters.
/// </summary>
/// <param name="Shortest">The purpose with the shortest window, or <see langword="null"/> when none is declared.</param>
/// <param name="CounterStore">
/// The kind of store: named, remote, or default; <see langword="null"/> under a custom limiter, whose store the module
/// cannot see.
/// </param>
/// <param name="CacheName">The named cache, when <paramref name="CounterStore"/> is <c>named</c>.</param>
internal sealed record RateLimitingCatalogue(
    IReadOnlyList<RateLimitingPurposeView> Purposes,
    RateLimitingPurposeView? Shortest,
    string? CounterStore,
    string? CacheName
);

/// <summary>The quota a lookup or a reset of an undeclared purpose names.</summary>
internal sealed record RateLimitingQuotaBody
{
    public int? Limit { get; init; }

    public long? WindowSeconds { get; init; }
}

/// <summary>A lookup of one subject: the purpose, the subject as the app passes it, and a quota for a custom purpose.</summary>
internal sealed record RateLimitingLookupBody
{
    public string? Purpose { get; init; }

    public string? Subject { get; init; }

    public RateLimitingQuotaBody? Quota { get; init; }
}

/// <summary>The quota a lookup read with.</summary>
internal sealed record RateLimitingQuotaView(int Limit, long WindowSeconds);

/// <summary>
/// One subject's current window for one purpose. It never carries the subject: <c>Fingerprint</c> names it.
/// </summary>
/// <param name="ClosesInSeconds">Seconds until the window closes, as read at lookup time; at least 1.</param>
/// <param name="Declared">Whether the purpose is declared, so the quota is the declared one.</param>
internal sealed record RateLimitingLookupView(
    string Purpose,
    string Fingerprint,
    long Count,
    int Limit,
    long Remaining,
    bool IsLockedOut,
    DateTimeOffset WindowStartsAt,
    DateTimeOffset WindowEndsAt,
    long ClosesInSeconds,
    RateLimitingQuotaView Quota,
    bool Declared
);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RateLimitingCatalogue))]
[JsonSerializable(typeof(RateLimitingLookupBody))]
[JsonSerializable(typeof(RateLimitingLookupView))]
internal sealed partial class RateLimitingDashboardJsonContext : JsonSerializerContext;
