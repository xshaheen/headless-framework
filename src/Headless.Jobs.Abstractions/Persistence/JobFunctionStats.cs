// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs;

/// <summary>What one function's runs did since a given instant, and what they are doing now.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record JobFunctionStats
{
    /// <summary>Gets the function.</summary>
    public required string Function { get; init; }

    /// <summary>Gets how many runs succeeded since the read's lower bound.</summary>
    public int Succeeded { get; init; }

    /// <summary>Gets how many runs failed since the read's lower bound.</summary>
    public int Failed { get; init; }

    /// <summary>Gets how many runs are running now.</summary>
    public int Running { get; init; }

    /// <summary>Gets how many runs are waiting to run now: idle or queued.</summary>
    public int Waiting { get; init; }

    /// <summary>Gets when the function's latest run ended, or <see langword="null"/> when none has.</summary>
    public DateTimeOffset? LastExecutedAt { get; init; }
}
