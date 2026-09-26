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
}

internal sealed class AttemptLimiterOptionsValidator : AbstractValidator<AttemptLimiterOptions>
{
    public AttemptLimiterOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();

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
