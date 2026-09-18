// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

/// <summary>
/// Storage-layer configuration shared by every messaging database provider. Messaging owns the
/// database naming, so a provider package never declares its own schema setting: PostgreSQL,
/// SQL Server, and both EF-context storage paths read the value configured here and each provider
/// validates it once against its own dialect's identifier rules.
/// </summary>
/// <remarks>
/// Configure it with <c>setup.ConfigureStorage(options =&gt; options.Schema = "…")</c> inside
/// <c>AddHeadlessMessaging</c>, or bind it from configuration through a provider's
/// <c>Use…(IConfiguration)</c> overload, which reads <see cref="SectionPath"/>. An explicit
/// <c>ConfigureStorage</c> call wins over the bound configuration value.
/// </remarks>
[PublicAPI]
public sealed class MessagingStorageOptions
{
    /// <summary>The configuration section these options bind from: <c>Headless:Messaging:Storage</c>.</summary>
    public const string SectionPath = "Headless:Messaging:Storage";

    /// <summary>The schema default, matching the feature name.</summary>
    public const string DefaultSchema = "messaging";

    /// <summary>
    /// Gets or sets the database schema that holds the messaging tables. Default: <c>"messaging"</c>.
    /// Validated at startup against the identifier rules of whichever storage provider is registered,
    /// so an invalid name fails the host rather than a later DDL statement.
    /// </summary>
    public string Schema { get; set; } = DefaultSchema;

    /// <summary>
    /// Copies every property to <paramref name="target"/>. Centralizes the property list so adding a
    /// property here only requires extending this method — the setup pipeline picks it up instead of
    /// silently dropping it from the DI-resolved instance.
    /// </summary>
    internal void CopyTo(MessagingStorageOptions target)
    {
        target.Schema = Schema;
    }
}
