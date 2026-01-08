// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Framework.Features.Resources;

public static class MessageDescriber
{
    public static ErrorDescriptor FeatureCurrentlyUnavailable()
    {
        return new(code: "g:feature_currently_not_available", description: Messages.g_feature_currently_unavailable);
    }
}
