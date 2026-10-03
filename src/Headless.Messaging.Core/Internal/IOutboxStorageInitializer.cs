// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization.Schema;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>Initializes one additional outbox's schema and names its tables.</summary>
internal interface IOutboxStorageInitializer : IStorageTableNames
{
    /// <summary>Applies the outbox's missing schema steps to its own database.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
