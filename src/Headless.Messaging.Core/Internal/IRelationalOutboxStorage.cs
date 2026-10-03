// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using Headless.Checks;
using Headless.Messaging.Persistence;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>A storage whose database identity can be compared with a unit of work's connection.</summary>
internal interface IRelationalOutboxStorage
{
    /// <summary>
    /// Builds, but never opens, a connection from this storage's own configuration. It is only read for its
    /// provider type, data source, and database name.
    /// </summary>
    DbConnection CreateIdentityConnection();
}
