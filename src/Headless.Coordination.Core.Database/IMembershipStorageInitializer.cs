// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting.Initialization;

namespace Headless.Coordination;

/// <summary>
/// Initializes the relational coordination schema with provider-specific race-safe DDL guards.
/// </summary>
internal interface IMembershipStorageInitializer : IInitializer;
