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

    /// <summary>Returns an error descriptor indicating that the provider <paramref name="providerName"/> is not defined for feature <paramref name="featureName"/>.</summary>
    /// <param name="featureName">The name of the feature.</param>
    /// <param name="providerName">The name of the missing provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor FeatureProviderNotDefined(string featureName, string providerName);

    /// <summary>Returns an error descriptor indicating that the provider identified by <paramref name="providerKey"/> is read-only and cannot be modified.</summary>
    /// <param name="providerKey">The key identifying the read-only provider.</param>
    /// <returns>An <see cref="ErrorDescriptor"/> describing the error.</returns>
    ErrorDescriptor ProviderIsReadonly(string providerKey);
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
    public ErrorDescriptor FeatureProviderNotDefined(string featureName, string providerName)
    {
        var description = string.Format(
            CultureInfo.InvariantCulture,
            FeatureMessages.features_provider_not_defined,
            featureName,
            providerName
        );

        var error = new ErrorDescriptor("features:provider-not-defined", description)
            .WithParam("featureName", featureName)
            .WithParam("providerName", providerName);

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
}
