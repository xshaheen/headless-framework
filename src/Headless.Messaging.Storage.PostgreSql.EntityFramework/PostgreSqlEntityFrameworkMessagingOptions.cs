// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Persistence;

namespace Headless.Messaging.Storage.PostgreSql.EntityFramework;

[PublicAPI]
public class PostgreSqlEntityFrameworkMessagingOptions
{
    /// <summary>
    /// Gets or sets the maximum length for the Owner column. Default is <see cref="DataStorageConstants.OwnerColumnMaxLength"/>.
    /// </summary>
    public int OwnerColumnMaxLength { get; set; } = DataStorageConstants.OwnerColumnMaxLength;

    /// <summary>
    /// Gets or sets whether consumers run on the transactional inbox tier for this EF-context storage path.
    /// Default <see langword="true" />: each consume attempt runs inside a unit of work over a transaction on
    /// the context, and the inbox row commits in that transaction together with the handler's saved changes and
    /// anything it publishes through <c>context.UnitOfWork.Outbox</c>; a rolled-back attempt discards all three.
    /// Set to <see langword="false" /> to drop to the durable-dedupe tier: the handler runs outside any
    /// transaction and <see cref="ConsumeContext.UnitOfWork" /> is <see langword="null" />. This switch affects
    /// only the receive side; enlisted publishing through <c>unit.Outbox</c> works either way.
    /// </summary>
    public bool EnableTransactionalInbox { get; set; } = true;
}
