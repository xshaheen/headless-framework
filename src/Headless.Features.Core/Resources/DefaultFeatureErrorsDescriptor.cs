// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Features.Resources;

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
}
