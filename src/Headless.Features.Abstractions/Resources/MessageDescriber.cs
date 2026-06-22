// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Features.Resources;

/// <summary>Factory for <see cref="ErrorDescriptor"/> instances used by the Features module.</summary>
public static class MessageDescriber
{
    /// <summary>Returns the error descriptor for a feature that is currently unavailable or disabled.</summary>
    /// <returns>An <see cref="ErrorDescriptor"/> with code <c>g:feature_currently_not_available</c>.</returns>
    public static ErrorDescriptor FeatureCurrentlyUnavailable()
    {
        return new(code: "g:feature_currently_not_available", description: Messages.g_feature_currently_unavailable);
    }
}
