// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Features.Resources;

/// <summary>Builds localized <see cref="ErrorDescriptor"/> instances for feature-management domain errors.</summary>
public interface IFeatureErrorsDescriptor
{
    /// <summary>Returns an error descriptor indicating that the feature named <paramref name="featureName"/> is not defined.</summary>
    /// <param name="featureName">The name of the undefined feature.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor FeatureIsNotDefined(string featureName);

    /// <summary>Returns an error descriptor indicating that no value provider named <paramref name="providerName"/> is registered.</summary>
    /// <param name="providerName">The name of the missing provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor ProviderNotFound(string providerName);

    /// <summary>Returns an error descriptor indicating that the provider identified by <paramref name="providerKey"/> is read-only and cannot be modified.</summary>
    /// <param name="providerKey">The key identifying the read-only provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor ProviderIsReadonly(string providerKey);

    /// <summary>
    /// Returns an error descriptor indicating that a write named a provider that the feature's
    /// <c>Providers</c> list does not include.
    /// </summary>
    /// <param name="featureName">The name of the feature.</param>
    /// <param name="providerName">The name of the provider the feature does not allow.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    /// <remarks>A default member, so an existing implementation keeps compiling and returns the built-in message.</remarks>
    ErrorDescriptor ProviderNotAllowed(string featureName, string providerName) =>
        DefaultFeatureErrorsDescriptor.CreateProviderNotAllowed(featureName, providerName);

    /// <summary>
    /// Returns the error for a versioned write whose features changed after the caller read their version.
    /// </summary>
    /// <param name="providerName">The provider name of the scope the write targeted.</param>
    /// <returns>An error with the code <c>g:feature_version_mismatch</c>.</returns>
    /// <remarks>A default member, so an existing implementation keeps compiling and returns the built-in message.</remarks>
    ErrorDescriptor VersionMismatch(string providerName) =>
        DefaultFeatureErrorsDescriptor.CreateVersionMismatch(providerName);
}

#pragma warning disable CA1863 // Use 'CompositeFormat'
/// <summary>Default implementation of <see cref="IFeatureErrorsDescriptor"/> that produces invariant-culture error messages.</summary>
public sealed class DefaultFeatureErrorsDescriptor : IFeatureErrorsDescriptor
{
    /// <inheritdoc/>
    public ErrorDescriptor FeatureIsNotDefined(string featureName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, "The feature '{0}' is undefined.", featureName);
        var error = new ErrorDescriptor("features:undefined", description).WithParam("featureName", featureName);

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor ProviderNotFound(string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            FeatureMessages.features_provider_not_found,
            providerName
        );

        var error = new ErrorDescriptor("features:provider-not-found", description).WithParam(
            "providerName",
            providerName
        );

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor ProviderIsReadonly(string providerKey)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            FeatureMessages.features_provider_readonly,
            providerKey
        );

        var error = new ErrorDescriptor("features:provider-readonly", description).WithParam(
            "providerKey",
            providerKey
        );

        return error;
    }

    /// <inheritdoc/>
    public ErrorDescriptor ProviderNotAllowed(string featureName, string providerName) =>
        CreateProviderNotAllowed(featureName, providerName);

    /// <inheritdoc/>
    public ErrorDescriptor VersionMismatch(string providerName) => CreateVersionMismatch(providerName);

    internal static ErrorDescriptor CreateVersionMismatch(string providerName)
    {
        return new ErrorDescriptor("g:feature_version_mismatch", FeatureMessages.features_version_mismatch).WithParam(
            "providerName",
            providerName
        );
    }

    internal static ErrorDescriptor CreateProviderNotAllowed(string featureName, string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            FeatureMessages.features_provider_not_allowed,
            featureName,
            providerName
        );

        return new ErrorDescriptor("features:provider-not-allowed", description)
            .WithParam("featureName", featureName)
            .WithParam("providerName", providerName);
    }
}
