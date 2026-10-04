// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>
/// Delegate type for job function handlers. The source generator emits implementations of this signature
/// that construct a <c>[Job]</c> class from the run's scope and invoke its <c>ExecuteAsync</c>.
/// </summary>
/// <param name="serviceProvider">The scoped service provider for this execution.</param>
/// <param name="context">Scheduling metadata and cooperative-cancel hook for this execution.</param>
/// <param name="cancellationToken">Token signalled when the job is cancelled or the host is shutting down.</param>
[PublicAPI]
public delegate Task JobFunctionDelegate(
    IServiceProvider serviceProvider,
    JobContext context,
    CancellationToken cancellationToken
);
