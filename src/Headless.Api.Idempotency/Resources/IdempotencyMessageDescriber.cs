// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Resources;

[PublicAPI]
internal static class IdempotencyMessageDescriber
{
    public static ErrorDescriptor KeyReused()
    {
        return new(code: "g:idempotency-key-reused", description: Messages.g_idempotency_key_reused);
    }

    public static ErrorDescriptor InFlight()
    {
        return new(code: "g:idempotency-in-flight", description: Messages.g_idempotency_in_flight);
    }

    public static ErrorDescriptor InFlightTimeout()
    {
        return new(code: "g:idempotency-in-flight-timeout", description: Messages.g_idempotency_in_flight_timeout);
    }

    public static ErrorDescriptor BodyTooLarge()
    {
        return new(code: "g:idempotency-body-too-large", description: Messages.g_idempotency_body_too_large);
    }
}
