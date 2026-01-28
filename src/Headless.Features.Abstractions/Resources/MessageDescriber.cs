// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Features.Resources;

public static class MessageDescriber
{
    public static ErrorDescriptor FeatureCurrentlyUnavailable()
    {
        return new(code: "g:feature_currently_not_available", description: Messages.g_feature_currently_unavailable);
    }
}
