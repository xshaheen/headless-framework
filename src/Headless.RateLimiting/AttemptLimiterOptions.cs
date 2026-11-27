// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.RateLimiting;

/// <summary>Options for <see cref="IAttemptLimiter"/>.</summary>
[PublicAPI]
public sealed class AttemptLimiterOptions
{
    /// <summary>The smallest accepted <see cref="SubjectKey"/>, in bytes, matching the HMAC-SHA256 output size.</summary>
    public const int MinSubjectKeyBytes = 32;

    /// <summary>
    /// Base64-encoded secret, at least <see cref="MinSubjectKeyBytes"/> bytes, used to HMAC subjects into cache keys.
    /// A keyed hash rather than a plain one, because phone numbers and email addresses are low-entropy enough to
    /// enumerate offline from a bare digest. Keep it out of source control. Rotating it starts every budget over.
    /// </summary>
    public string SubjectKey { get; set; } = "";

    /// <summary>
    /// The leading segment of every counter key. Defaults to <c>"attempts"</c>; change it to separate applications
    /// that share one cache.
    /// </summary>
    public string KeyPrefix { get; set; } = "attempts";

    /// <summary>
    /// The named cache instance (<c>setup.AddNamed(name, ...)</c>) that holds the counters, or <see langword="null"/>
    /// for the default <c>ICache</c>. Use a store that does not evict and survives restarts: a lost counter reopens
    /// its budget. The host refuses to start when no cache is registered under this name.
    /// </summary>
    public string? CacheName { get; set; }

    /// <summary>
    /// The purposes the application acquires with and the quota each one uses, keyed by purpose with ordinal
    /// comparison. Informational: the limiter accepts any purpose, declared or not. Declare each flow here so an
    /// operator screen or a diagnostic can list the purposes and look a subject up with the right quota.
    /// </summary>
    /// <remarks>
    /// Read the quota for <see cref="IAttemptLimiter.AcquireAsync"/> from here too, so the declared value and the
    /// enforced value cannot drift. A configuration section binds each entry's <c>Limit</c> and <c>Window</c>. A purpose
    /// containing <c>:</c> cannot be a configuration key, so declare it in code.
    /// </remarks>
    public IDictionary<string, AttemptQuota> Purposes { get; } =
        new Dictionary<string, AttemptQuota>(StringComparer.Ordinal);
}

internal sealed class AttemptLimiterOptionsValidator : AbstractValidator<AttemptLimiterOptions>
{
    public AttemptLimiterOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();

        // Null selects the default cache; an empty or blank name (an unset configuration value) names no instance.
        RuleFor(x => x.CacheName).NotEmpty().When(x => x.CacheName is not null);

        RuleForEach(x => x.Purposes)
            .ChildRules(entry =>
            {
                entry
                    .RuleFor(x => x.Key)
                    .NotEmpty()
                    .Must(static name => !string.IsNullOrWhiteSpace(name))
                    .WithMessage("A declared attempt purpose must not be blank.");

                entry.RuleFor(x => x.Value).NotNull().WithMessage("A declared attempt purpose needs a quota.");
            });

        RuleFor(x => x.SubjectKey)
            .NotEmpty()
            .Must(_HasMinimumLength)
            .WithMessage(
                $"'{nameof(AttemptLimiterOptions.SubjectKey)}' must be base64 encoding at least "
                    + $"{AttemptLimiterOptions.MinSubjectKeyBytes} bytes."
            );
    }

    private static bool _HasMinimumLength(string value)
    {
        var buffer = new byte[(value.Length * 3 / 4) + 3];

        return Convert.TryFromBase64String(value, buffer, out var written)
            && written >= AttemptLimiterOptions.MinSubjectKeyBytes;
    }
}
