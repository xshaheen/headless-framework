// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Settings.Resources;

/// <summary>Describes localised error descriptors for settings management failures.</summary>
public interface ISettingErrorsDescriptor
{
    /// <summary>Returns an error descriptor for a setting that is not defined.</summary>
    /// <param name="settingName">The name of the undefined setting.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor NotDefined(string settingName);

    /// <summary>Returns an error descriptor when a requested setting value provider is not registered.</summary>
    /// <param name="providerName">The name of the missing provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor ProviderNotFound(string providerName);

    /// <summary>Returns an error descriptor when a setting value provider does not support write operations.</summary>
    /// <param name="providerKey">The key identifying the read-only provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor ProviderIsReadonly(string providerKey);

    /// <summary>Returns an error descriptor when the current user is not available in the current scope.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor CurrentUserNotAvailable();

    /// <summary>Returns an error descriptor when the current tenant is not available in the current scope.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor CurrentTenantNotAvailable();

    /// <summary>Returns an error descriptor when decryption of an encrypted setting value fails.</summary>
    /// <param name="settingName">The name of the setting whose value could not be decrypted.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor DecryptionFailed(string settingName);
}
