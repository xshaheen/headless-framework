// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Features.Resources;

public interface IFeatureErrorsDescriptor
{
    ValueTask<ErrorDescriptor> FeatureIsNotDefined(string featureName);

    ValueTask<ErrorDescriptor> FeatureProviderNotDefined(string featureName, string providerName);

    ValueTask<ErrorDescriptor> ProviderIsReadonly(string providerKey);
}

#pragma warning disable CA1863 // Use 'CompositeFormat'
public sealed class DefaultFeatureErrorsDescriptor : IFeatureErrorsDescriptor
{
    public ValueTask<ErrorDescriptor> FeatureIsNotDefined(string featureName)
    {
        var description = string.Format(CultureInfo.InvariantCulture, "The feature '{0}' is undefined.", featureName);
        var error = new ErrorDescriptor("features:undefined", description).WithParam("featureName", featureName);

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> FeatureProviderNotDefined(string featureName, string providerName)
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

        return ValueTask.FromResult(error);
    }

    public ValueTask<ErrorDescriptor> ProviderIsReadonly(string providerKey)
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

        return ValueTask.FromResult(error);
    }
}
