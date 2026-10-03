// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Idempotency;

namespace Headless.Api.Idempotency;

internal sealed class IdempotencyContext(string headerKey, string scope, string key, IdempotentAdmission admission)
    : IIdempotencyContext
{
    public string HeaderKey { get; } = headerKey;

    public string Scope { get; } = scope;

    public string Key { get; } = key;

    public IdempotentAdmission Admission { get; } = admission;

    public long Generation { get; } = admission.Generation!.Value;

    public bool IsTakeover => Admission.IsTakeover;

    public IdempotentRecoveryPoint? RecoveryPoint => Admission.RecoveryPoint;
}
