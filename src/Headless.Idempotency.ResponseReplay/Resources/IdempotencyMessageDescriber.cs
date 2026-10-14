// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Headless.Idempotency.Resources;

internal static class IdempotencyMessageDescriber
{
    public static ErrorDescriptor KeyReused()
    {
        return new(code: HttpIdempotencyErrorCodes.KeyReused, description: Messages.g_idempotency_key_reused);
    }

    public static ErrorDescriptor InFlight()
    {
        return new(code: HttpIdempotencyErrorCodes.InFlight, description: Messages.g_idempotency_in_flight);
    }

    public static ErrorDescriptor InFlightTimeout()
    {
        return new(
            code: HttpIdempotencyErrorCodes.InFlightTimeout,
            description: Messages.g_idempotency_in_flight_timeout
        );
    }

    public static ErrorDescriptor KeyRequired()
    {
        return new(code: HttpIdempotencyErrorCodes.KeyRequired, description: Messages.g_idempotency_key_required);
    }

    public static ErrorDescriptor BodyTooLarge()
    {
        return new(code: HttpIdempotencyErrorCodes.BodyTooLarge, description: Messages.g_idempotency_body_too_large);
    }

    public static ErrorDescriptor KeyMalformed()
    {
        return new(code: HttpIdempotencyErrorCodes.KeyMalformed, description: Messages.g_idempotency_key_malformed);
    }
}
