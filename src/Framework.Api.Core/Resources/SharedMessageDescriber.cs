// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Kernel.Primitives;

namespace Framework.Api.Core.Resources;

public static class SharedMessageDescriber
{
    public static class General
    {
        public static ErrorDescriptor ConcurrencyFailure()
        {
            return new(code: "g:concurrency_failure", description: Messages.g_concurrency_failure);
        }

        public static ErrorDescriptor UnknownError()
        {
            return new(code: "g:unknown_error", description: Messages.g_unknown_error);
        }

        public static ErrorDescriptor ObsoleteApi()
        {
            return new(code: "g:obsolete_api", description: Messages.g_obsolete_api);
        }

        public static ErrorDescriptor FeatureCurrentlyUnavailable()
        {
            return new(
                code: "g:feature_currently_not_available",
                description: Messages.g_feature_currently_unavailable
            );
        }
    }
}
