// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

/// <summary>
/// Storage-layer configuration shared by every messaging database provider. Messaging owns the
/// database naming, so a provider package never declares its own schema setting: PostgreSQL,
/// SQL Server, and both EF-context storage paths read the value configured here and each provider
/// validates it once against its own dialect's identifier rules.
/// </summary>
/// <remarks>
/// Configure it inside <c>AddHeadlessMessaging</c> with either
/// <c>setup.ConfigureStorage(options =&gt; options.Schema = "…")</c> or
/// <c>setup.ConfigureStorage(configuration.GetSection("Headless:Messaging:Storage"))</c>. Both register
/// in call order, so the last one applied wins.
/// </remarks>
[PublicAPI]
public sealed class MessagingStorageOptions
{
    /// <summary>The schema default, matching the feature name.</summary>
    public const string DefaultSchema = "messaging";

    /// <summary>
    /// Gets or sets the database schema that holds the messaging tables. Default: <c>"messaging"</c>.
    /// Validated at startup against the identifier rules of whichever storage provider is registered,
    /// so an invalid name fails the host rather than a later DDL statement.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;
}
