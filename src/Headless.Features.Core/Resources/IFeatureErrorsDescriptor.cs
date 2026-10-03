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
}
