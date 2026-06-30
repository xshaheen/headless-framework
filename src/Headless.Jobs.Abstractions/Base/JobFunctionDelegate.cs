// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Delegate type for job function handlers. The source generator emits implementations of this signature
/// that instantiate the job class from DI and invoke its <c>[JobFunction]</c>-annotated method.
/// </summary>
/// <param name="cancellationToken">Token signalled when the job is cancelled or the host is shutting down.</param>
/// <param name="serviceProvider">The scoped service provider for this execution.</param>
/// <param name="context">Scheduling metadata and cooperative-cancel hook for this execution.</param>
public delegate Task JobFunctionDelegate(
    CancellationToken cancellationToken,
    IServiceProvider serviceProvider,
    JobFunctionContext context
);
