// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.AspNetCore.DataProtection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// Signs the scope this node freezes for an Operation another node runs over this node's rows, and reads it back on
/// each chunk, so a chunk runs only over a scope this node counted within its own limit.
/// </summary>
/// <remarks>
/// It uses the host's key ring, like the shell's Operation tokens, so every instance of one node must share it. Its
/// purpose differs from the shell's, so neither kind of token is accepted in place of the other.
/// </remarks>
internal sealed class PeerOperationScopeProtector(IDataProtectionProvider dataProtection, TimeProvider timeProvider)
{
    private const string _Purpose = "Headless.Messaging.Dashboard.PeerOperationScope.v1";

    public OperationScopeTokenProtector Tokens { get; } = new(dataProtection, timeProvider, _Purpose);
}
