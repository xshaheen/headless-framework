// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.ResourceLocks;

public sealed class ResourceLockOptions
{
    /// <summary>Resource lock key prefix.</summary>
    public string KeyPrefix { get; set; } = "resource-lock:";
}

public sealed class ResourceLockOptionsValidator : AbstractValidator<ResourceLockOptions>
{
    public ResourceLockOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotEmpty();
    }
}
