// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Api.Idempotency.Resources;

internal static class IdempotencyMessageDescriber
{
    public static ErrorDescriptor KeyReused()
    {
        return new(code: IdempotencyErrorCodes.KeyReused, description: Messages.g_idempotency_key_reused);
    }

    public static ErrorDescriptor InFlight()
    {
        return new(code: IdempotencyErrorCodes.InFlight, description: Messages.g_idempotency_in_flight);
    }

    public static ErrorDescriptor InFlightTimeout()
    {
        return new(code: IdempotencyErrorCodes.InFlightTimeout, description: Messages.g_idempotency_in_flight_timeout);
    }

    public static ErrorDescriptor BodyTooLarge()
    {
        return new(code: IdempotencyErrorCodes.BodyTooLarge, description: Messages.g_idempotency_body_too_large);
    }

    public static ErrorDescriptor KeyMalformed()
    {
        return new(code: IdempotencyErrorCodes.KeyMalformed, description: Messages.g_idempotency_key_malformed);
    }
}
