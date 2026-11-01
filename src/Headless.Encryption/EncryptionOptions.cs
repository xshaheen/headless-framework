// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Encryption;

/// <summary>Configures the in-process cache of unwrapped data keys.</summary>
[PublicAPI]
public sealed class EncryptionOptions
{
    /// <summary>
    /// Gets or sets how long an unwrapped data key, and a scope's active key id, stay cached. It bounds how long
    /// another host keeps using a key after <see cref="IDataKeyManager.ShredAsync" /> or keeps encrypting with the
    /// previous version after <see cref="IDataKeyManager.RotateAsync" />. The default is 1 hour.
    /// </summary>
    public TimeSpan KeyCacheDuration { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the most unwrapped data keys cached at once; past it, the keys cached earliest are dropped and
    /// unwrapped again on next use. The default is 10,000.
    /// </summary>
    public int KeyCacheSize { get; set; } = 10_000;
}

internal sealed class EncryptionOptionsValidator : AbstractValidator<EncryptionOptions>
{
    public EncryptionOptionsValidator()
    {
        RuleFor(x => x.KeyCacheDuration).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromDays(1));
        RuleFor(x => x.KeyCacheSize).GreaterThan(0);
    }
}
