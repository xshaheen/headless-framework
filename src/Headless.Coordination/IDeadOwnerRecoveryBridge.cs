// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Coordination;

/// <summary>
/// Non-generic marker implemented by every closed <see cref="DeadOwnerRecoveryBridge{TReclaimer}"/>. The bridge
/// itself is internal infrastructure, so this public marker lets test and tooling code identify the hosted bridge
/// in the <c>IHostedService</c> graph by type (rename-safe) instead of by a fragile type-name string match.
/// </summary>
[PublicAPI]
public interface IDeadOwnerRecoveryBridge;
