// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Api.Middlewares;

public sealed class IdempotencyMiddlewareOptions
{
    /// <summary>
    /// Gets or sets the time until the idempotency key expires.
    /// </summary>
    public TimeSpan? IdempotencyKeyExpiration { get; set; } = TimeSpan.FromHours(1);
}

public sealed class IdempotencyMiddlewareOptionsValidator : AbstractValidator<IdempotencyMiddlewareOptions>
{
    public IdempotencyMiddlewareOptionsValidator()
    {
        RuleFor(x => x.IdempotencyKeyExpiration).GreaterThan(TimeSpan.Zero);
    }
}
