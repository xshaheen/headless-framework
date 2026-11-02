// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Encryption;
using Headless.Settings.Resources;
using Microsoft.Extensions.Logging;

namespace Headless.Settings;

/// <summary>Encrypts and decrypts setting values for settings that have <c>IsEncrypted</c> set to <see langword="true"/>.</summary>
public interface ISettingEncryptionService
{
    /// <summary>Encrypts <paramref name="plainValue"/> for the given <paramref name="settingDefinition"/>.</summary>
    /// <param name="settingDefinition">The setting whose value is being encrypted.</param>
    /// <param name="plainValue">The plain-text value to encrypt, or <see langword="null"/> / empty to pass through unchanged.</param>
    /// <param name="providerName">The value provider that stores the value; it decides the key scope.</param>
    /// <param name="providerKey">The provider key the value is stored under, such as a tenant id, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encrypted value, or the original value when it is <see langword="null"/> or empty.</returns>
    ValueTask<string?> EncryptAsync(
        SettingDefinition settingDefinition,
        string? plainValue,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );

    /// <summary>Decrypts <paramref name="encryptedValue"/> for the given <paramref name="settingDefinition"/>.</summary>
    /// <param name="settingDefinition">The setting whose value is being decrypted.</param>
    /// <param name="encryptedValue">The encrypted value to decrypt, or <see langword="null"/> / empty to pass through unchanged.</param>
    /// <param name="providerName">The value provider the value was read from; it decides the key scope.</param>
    /// <param name="providerKey">The provider key the value was read under, such as a tenant id, or <see langword="null"/> for the current one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The decrypted plain-text value, or the original value when it is <see langword="null"/> or empty.</returns>
    /// <exception cref="Headless.ConflictException">Decryption of the setting value fails.</exception>
    ValueTask<string?> DecryptAsync(
        SettingDefinition settingDefinition,
        string? encryptedValue,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Default implementation of <see cref="ISettingEncryptionService"/> backed by <see cref="IFieldEncryptor"/>. Values
/// stored by the tenant provider are encrypted under that tenant's key, so shredding the tenant's keys also destroys
/// its encrypted settings; every other provider uses the host key.
/// </summary>
public sealed class SettingEncryptionService(
    ISettingErrorsDescriptor errorsDescriptor,
    ILogger<SettingEncryptionService> logger,
    IFieldEncryptor? fieldEncryptor = null
) : ISettingEncryptionService
{
    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">No <see cref="IFieldEncryptor"/> is registered.</exception>
    public async ValueTask<string?> EncryptAsync(
        SettingDefinition settingDefinition,
        string? plainValue,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (plainValue.IsNullOrEmpty())
        {
            return plainValue;
        }

        var encryptor = _GetEncryptor(settingDefinition);

        try
        {
            var encrypted = await encryptor
                .EncryptAsync(
                    plainValue,
                    _CreateContext(settingDefinition, providerName, providerKey),
                    cancellationToken
                )
                .ConfigureAwait(false);

            return encrypted.ToBase64Url();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogFailedToEncryptSettingValue(e, settingDefinition.Name);

            throw;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="Headless.ConflictException">The stored value is not a valid frame or fails to decrypt.</exception>
    /// <exception cref="InvalidOperationException">No <see cref="IFieldEncryptor"/> is registered.</exception>
    public async ValueTask<string?> DecryptAsync(
        SettingDefinition settingDefinition,
        string? encryptedValue,
        string providerName,
        string? providerKey,
        CancellationToken cancellationToken = default
    )
    {
        if (encryptedValue.IsNullOrEmpty())
        {
            return encryptedValue;
        }

        var encryptor = _GetEncryptor(settingDefinition);

        if (!EncryptedValue.TryParse(encryptedValue, out var frame))
        {
            logger.LogMalformedEncryptedSettingValue(settingDefinition.Name);

            throw new ConflictException(errorsDescriptor.DecryptionFailed(settingDefinition.Name));
        }

        try
        {
            return await encryptor
                .DecryptStringAsync(
                    frame,
                    _CreateContext(settingDefinition, providerName, providerKey),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (FieldDecryptionException e)
        {
            logger.LogFailedToDecryptSettingValue(e, settingDefinition.Name);

            throw new ConflictException(errorsDescriptor.DecryptionFailed(settingDefinition.Name));
        }
    }

    private IFieldEncryptor _GetEncryptor(SettingDefinition settingDefinition)
    {
        // Resolved lazily so a host with no encrypted setting needs no encryption setup at all.
        return fieldEncryptor
            ?? throw new InvalidOperationException(
                $"Setting '{settingDefinition.Name}' is encrypted, but no {nameof(IFieldEncryptor)} is registered. "
                    + "Call AddHeadlessEncryption(...) to register one."
            );
    }

    private static FieldContext _CreateContext(
        SettingDefinition settingDefinition,
        string providerName,
        string? providerKey
    )
    {
        var purpose = "settings:" + settingDefinition.Name;

        // A null tenant key means the current tenant, which a null FieldContext.Scope also resolves to.
        return string.Equals(providerName, SettingValueProviderNames.Tenant, StringComparison.Ordinal)
            ? new FieldContext(purpose) { Scope = providerKey }
            : FieldContext.Host(purpose);
    }
}

internal static partial class SettingEncryptionServiceLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "FailedToEncryptSettingValue",
        Level = LogLevel.Warning,
        Message = "Failed to encrypt setting value: {SettingDefinition}"
    )]
    internal static partial void LogFailedToEncryptSettingValue(
        this ILogger logger,
        Exception exception,
        string settingDefinition
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "FailedToDecryptSettingValue",
        Level = LogLevel.Error,
        Message = "Failed to decrypt setting value: {SettingDefinition}"
    )]
    internal static partial void LogFailedToDecryptSettingValue(
        this ILogger logger,
        Exception exception,
        string settingDefinition
    );

    [LoggerMessage(
        EventId = 3,
        EventName = "MalformedEncryptedSettingValue",
        Level = LogLevel.Error,
        Message = "Stored value of encrypted setting {SettingDefinition} is not an encrypted frame"
    )]
    internal static partial void LogMalformedEncryptedSettingValue(this ILogger logger, string settingDefinition);
}
