// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.AuditLog;

/// <summary>
/// The one JSON format every audit-log storage provider writes <c>OldValues</c>, <c>NewValues</c>, and
/// <c>ChangedFields</c> in, so an entry reads back the same from every provider.
/// </summary>
/// <remarks>
/// The format is part of the stored audit trail, so it is fixed here rather than taken from the host's
/// <c>IJsonOptionsProvider</c>: registering other JSON options must never change how history is written. Keys are
/// property names and stay as written, so <c>NewValues[ChangedFields[0]]</c> finds its value, and values read back as
/// <see cref="JsonElement"/>.
/// </remarks>
internal static class AuditLogJsonOptions
{
    public static JsonSerializerOptions Instance { get; } = _Create();

    private static JsonSerializerOptions _Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }
}
