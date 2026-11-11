// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries;

/// <summary>
/// Thrown when a run of a live query recorded no dependency. No announced change could ever re-run that query, so
/// the subscription fails instead of staying open and silent.
/// </summary>
/// <remarks>
/// The usual causes are a query that reads a source nothing tracks without declaring a key through
/// <see cref="ILiveQueryDependencies.DependsOn" />, and a query that does its reads on a detached execution flow,
/// such as <c>Task.Run</c> with a suppressed context, where the recording does not follow.
/// </remarks>
[PublicAPI]
public sealed class LiveQueryHasNoDependenciesException()
    : InvalidOperationException(
        "The live query recorded no dependency, so no change could ever re-run it. Declare one with "
            + "ILiveQueryDependencies.DependsOn, or read through a tracked data source on the query's own "
            + "execution flow."
    );
