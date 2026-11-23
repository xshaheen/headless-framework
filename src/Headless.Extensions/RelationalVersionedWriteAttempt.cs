// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless;

/// <summary>The state of one attempt of <see cref="RelationalVersionedWrite.ExecuteAsync" />.</summary>
[PublicAPI]
public sealed class RelationalVersionedWriteAttempt
{
    internal bool CommitStarted { get; private set; }

    internal string? ResultVersion { get; private set; }

    /// <summary>
    /// Records the version the write leaves, just before the attempt commits. A failure after this call has an unknown
    /// outcome and is settled by reading the group again; it is never retried.
    /// </summary>
    /// <param name="resultVersion">The group's version read inside the transaction after the write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resultVersion" /> is <see langword="null" />.</exception>
    public void BeginCommit(string resultVersion)
    {
        Argument.IsNotNull(resultVersion);

        ResultVersion = resultVersion;
        CommitStarted = true;
    }
}
