// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// A marker service used internally to verify that a message storage extension (e.g., SQL Server, PostgreSQL, MySQL, MongoDB)
/// has been registered on a <see cref="IServiceCollection"/>.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="MessageStorageMarkerService"/> class with the specified storage name.
/// </remarks>
/// <param name="name">The name identifier for the storage extension.</param>
internal sealed class MessageStorageMarkerService(string name)
{
    /// <summary>
    /// Gets or sets the name identifier for the storage extension (e.g., "SqlServer", "PostgreSql", "MySql").
    /// </summary>
    public string Name { get; set; } = name;
}
