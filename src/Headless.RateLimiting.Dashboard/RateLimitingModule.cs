// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;

namespace Headless.RateLimiting;

/// <summary>
/// The Rate limits dashboard module: the attempt-limit purposes the app declares, a lookup of one subject's current
/// window, and a confirmed, reasoned reset of that window. The subject travels only in a request body or a command
/// argument; every answer and the operator log carry its fingerprint instead.
/// </summary>
/// <remarks>
/// Its sidebar count <c>purposes</c> is the number of declared purposes. Its own API is mapped by
/// <see cref="RateLimitingApi"/>.
/// </remarks>
internal sealed class RateLimitingModule : IDashboardModule, IDashboardCountSource
{
    /// <summary>The sidebar count key of the declared purposes.</summary>
    public const string PurposesCountKey = "purposes";

    private readonly RateLimitingApi _api;
    private readonly IReadOnlyDictionary<string, DashboardCount> _counts;

    public RateLimitingModule(
        IAttemptLimiter limiter,
        AttemptSubjectFingerprinter fingerprinter,
        AttemptLimiterOptions limiterOptions,
        RateLimitingCatalogue catalogue
    )
    {
        Argument.IsNotNull(limiter);
        Argument.IsNotNull(fingerprinter);
        Argument.IsNotNull(limiterOptions);
        Argument.IsNotNull(catalogue);

        _api = new RateLimitingApi(limiter, fingerprinter, limiterOptions, catalogue);
        _counts = new Dictionary<string, DashboardCount>(StringComparer.Ordinal)
        {
            [PurposesCountKey] = new() { Count = catalogue.Purposes.Count },
        };
    }

    public string Id => RateLimitingDashboardOptions.ModuleId;

    public string DisplayName => "Rate limits";

    public DashboardModuleAssets Assets { get; } = RateLimitingAssets.Create();

    public void MapEndpoints(DashboardModuleEndpoints endpoints)
    {
        Argument.IsNotNull(endpoints);

        _api.Map(endpoints);
    }

    public Task<IReadOnlyDictionary<string, DashboardCount>> GetCountsAsync(
        DashboardCountsRequest request,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult(_counts);
    }
}
