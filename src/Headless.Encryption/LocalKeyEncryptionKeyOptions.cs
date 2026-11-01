// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Encryption;

/// <summary>
/// Configures the local key-encryption key: one or more 32-byte AES keys held by the host itself, typically from a
/// secret store, each under an id that is stored with every data key it wraps.
/// </summary>
/// <remarks>
/// To rotate, add a new key, point <see cref="ActiveKeyId" /> at it, run <see cref="IDataKeyManager.RewrapAsync" />,
/// and remove the old key only after the re-wrap reports 0. Whoever reads these keys can read every encrypted value,
/// so keep them out of source control and out of the database.
/// </remarks>
[PublicAPI]
public sealed class LocalKeyEncryptionKeyOptions
{
    /// <summary>Gets the keys by id: each value is a 32-byte key in base64. Generate one with <c>openssl rand -base64 32</c>.</summary>
    public IDictionary<string, string> Keys { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets the id of the key that wraps new data keys. Required, and must be one of <see cref="Keys" />.</summary>
    public string ActiveKeyId { get; set; } = string.Empty;
}

internal sealed class LocalKeyEncryptionKeyOptionsValidator : AbstractValidator<LocalKeyEncryptionKeyOptions>
{
    public LocalKeyEncryptionKeyOptionsValidator()
    {
        RuleFor(x => x.Keys).NotEmpty().WithMessage("At least one local key-encryption key is required.");
        RuleFor(x => x.ActiveKeyId)
            .NotEmpty()
            .Must((options, id) => options.Keys.ContainsKey(id))
            .WithMessage("ActiveKeyId must name one of the configured local keys.");
        RuleForEach(x => x.Keys)
            .Must(static pair =>
                !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= DataKeyFieldLimits.KekIdMaxLength
            )
            .WithMessage(
                $"A local key id must be non-blank and at most {DataKeyFieldLimits.KekIdMaxLength} characters."
            )
            .Must(static pair => LocalKeyEncryptionKeyProvider.TryDecodeKey(pair.Value, out _))
            .WithMessage(static (_, pair) => $"Local key '{pair.Key}' must be a 32-byte key in base64.");
    }
}
