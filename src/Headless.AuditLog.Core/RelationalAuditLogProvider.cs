// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Constants;
using Headless.Hosting.Initialization.Schema;
using Headless.MultiTenancy;
using Headless.Serializer;
using Headless.Sql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Headless.AuditLog;

/// <summary>How one relational provider stores the audit log, beyond what its dialect already says.</summary>
/// <param name="Provider">The database whose identifier rules the storage options must satisfy.</param>
/// <param name="DefaultJsonColumnType">The JSON column type used when none is configured.</param>
/// <param name="JsonColumnTypes">The JSON column types the provider accepts.</param>
/// <param name="JsonColumnTypeMessage">The validation message for any other JSON column type.</param>
/// <param name="CreateCreatedAtBinder">
/// Builds the <c>CreatedAt</c> binder for the configured column type, or <see langword="null"/> to bind every instant as
/// the dialect's timestamp.
/// </param>
internal sealed record RelationalAuditLogProvider(
    StorageProvider Provider,
    AuditLogJsonColumnType DefaultJsonColumnType,
    IReadOnlyCollection<AuditLogJsonColumnType> JsonColumnTypes,
    string JsonColumnTypeMessage,
    Func<AuditLogStorageOptions, AuditLogCreatedAtBinder>? CreateCreatedAtBinder = null
);
