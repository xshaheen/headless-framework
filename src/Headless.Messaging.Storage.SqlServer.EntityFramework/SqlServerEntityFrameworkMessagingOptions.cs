// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.SqlServer.EntityFramework;

[PublicAPI]
public class SqlServerEntityFrameworkMessagingOptions
{
    /// <summary>
    /// Gets or sets the maximum length for the Owner column. Default is <see cref="DataStorageConstants.OwnerColumnMaxLength"/>.
    /// </summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets whether the transactional (atomic) outbox is enabled for this EF-context storage path.
    /// Default <see langword="true" />: a publish issued inside a coordinated transaction writes its outbox row in
    /// the same DB transaction and is discarded on rollback (commit coordination, the interceptor attach, and the
    /// startup self-probe are auto-wired). Set to <see langword="false" /> to opt out: publishes then store first and
    /// the relay dispatches them, never atomically with the caller's transaction. No effect on the raw-ADO storage
    /// paths (connection-string overloads), which are never transactional by default.
    /// </summary>
    public bool EnableTransactionalOutbox { get; set; } = true;
}
