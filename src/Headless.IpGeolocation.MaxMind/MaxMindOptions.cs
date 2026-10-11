// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.IpGeolocation;

/// <summary>Configures the MaxMind IP geolocation provider.</summary>
[PublicAPI]
public sealed class MaxMindOptions
{
    /// <summary>The name of the <see cref="HttpClient" /> that downloads database updates.</summary>
    public const string HttpClientName = "Headless.IpGeolocation.MaxMind";

    /// <summary>
    /// The directory that holds the databases, one <c>&lt;EditionId&gt;.mmdb</c> file per edition. The updater
    /// creates it when missing.
    /// </summary>
    public string DatabaseDirectory { get; set; } = "";

    /// <summary>
    /// The location edition: <c>GeoLite2-City</c> (the default), <c>GeoLite2-Country</c>, or a paid <c>GeoIP2-*</c>
    /// City or Country edition. <see langword="null" /> or empty leaves the location fields empty.
    /// </summary>
    public string? LocationEditionId { get; set; } = "GeoLite2-City";

    /// <summary>
    /// The autonomous system edition, <c>GeoLite2-ASN</c> by default. <see langword="null" /> or empty leaves the
    /// autonomous system fields empty.
    /// </summary>
    public string? AsnEditionId { get; set; } = "GeoLite2-ASN";

    /// <summary>The MaxMind account ID. Set it with <see cref="LicenseKey" /> to keep the databases current.</summary>
    public string? AccountId { get; set; }

    /// <summary>
    /// A MaxMind license key. Without one the provider only reads the files already in
    /// <see cref="DatabaseDirectory" />.
    /// </summary>
    public string? LicenseKey { get; set; }

    /// <summary>The locales to read names in, in order of preference.</summary>
    public IList<string> Locales { get; set; } = ["en"];

    /// <summary>How often to ask MaxMind whether a newer database exists. Defaults to 12 hours.</summary>
    public TimeSpan UpdateCheckInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>How long to wait before checking again after a failed check or download. Defaults to 30 minutes.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>The MaxMind download server.</summary>
    public string DownloadEndpoint { get; set; } = "https://download.maxmind.com/";

    /// <summary>The time limit for downloading one database.</summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromMinutes(10);

    internal bool HasCredentials => !string.IsNullOrEmpty(AccountId) && !string.IsNullOrEmpty(LicenseKey);
}

internal sealed class MaxMindOptionsValidator : AbstractValidator<MaxMindOptions>
{
    public MaxMindOptionsValidator()
    {
        RuleFor(x => x.DatabaseDirectory).NotEmpty();
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.LocationEditionId) || !string.IsNullOrEmpty(x.AsnEditionId))
            .WithMessage("Set LocationEditionId, AsnEditionId, or both.");
        RuleFor(x => x)
            .Must(x => string.IsNullOrEmpty(x.AccountId) == string.IsNullOrEmpty(x.LicenseKey))
            .WithMessage("Set both AccountId and LicenseKey to download updates, or neither to read local files only.");
        RuleFor(x => x.Locales).NotEmpty();
        RuleForEach(x => x.Locales).NotEmpty();
        // MaxMind rate-limits downloads (GeoLite accounts get 30 a day), so checks stay at most hourly.
        RuleFor(x => x.UpdateCheckInterval).GreaterThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.RetryDelay).GreaterThanOrEqualTo(TimeSpan.FromMinutes(1));
        RuleFor(x => x.DownloadTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.DownloadEndpoint).HttpsOrLoopbackHttpUrl();
    }
}
