// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;

namespace Tests;

public interface ICommitCoordinationFixture
{
    /// <summary>Opens a single root scope on a fresh ambient stack.</summary>
    ValueTask<ICommitScope> BeginScopeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates a fresh ambient stack. Provider-portable scenarios that need to open a root and a
    /// joining child on the same stack (nesting, slot isolation) build a factory over it and read
    /// the ambient <see cref="ICurrentCommitCoordinator.Current" /> back. Returns the interface because
    /// the concrete stack is Core-internal; harness-side callers cast under <c>InternalsVisibleTo</c>.
    /// </summary>
    ICurrentCommitCoordinator CreateStack();

    /// <summary>A stub service provider captured for callback drain.</summary>
    IServiceProvider Services { get; }
}
