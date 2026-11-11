// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.LiveQueries.Resources;

internal static class LiveEndpointsMessageDescriber
{
    public static ErrorDescriptor NoDependencies()
    {
        return new(code: LiveEndpointsErrorCodes.NoDependencies, description: Messages.g_live_query_no_dependencies);
    }

    public static ErrorDescriptor RunFailed()
    {
        return new(code: LiveEndpointsErrorCodes.RunFailed, description: Messages.g_live_query_run_failed);
    }

    public static ErrorDescriptor ResponseNotJson()
    {
        return new(code: LiveEndpointsErrorCodes.ResponseNotJson, description: Messages.g_live_query_response_not_json);
    }

    public static ErrorDescriptor Unauthorized()
    {
        return new(code: LiveEndpointsErrorCodes.Unauthorized, description: Messages.g_live_query_unauthorized);
    }

    public static ErrorDescriptor StreamLimitReached()
    {
        return new(
            code: LiveEndpointsErrorCodes.StreamLimitReached,
            description: Messages.g_live_query_stream_limit_reached
        );
    }
}
