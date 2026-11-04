// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;

namespace Headless.AuditLog;

/// <summary>The JSON format every audit-log storage provider writes and reads an entry's values with.</summary>
[PublicAPI]
public static class AuditLogJson
{
    /// <summary>
    /// The options that serialize <c>OldValues</c>, <c>NewValues</c>, and <c>ChangedFields</c> on every provider: the
    /// internal preset (<see cref="JsonConstants.DefaultInternalJsonOptions" />), with keys as captured, enums by name
    /// so a reordered enum never changes what history says, and values read back as <see cref="JsonElement" />. It
    /// never follows the host's JSON options.
    /// </summary>
    /// <remarks>
    /// Anything that must reproduce the stored text, such as an integrity hash computed when an entry is written and
    /// checked after it is read back, serializes with these options rather than naming a preset itself, so it follows
    /// the format if the framework changes it. The instance is read-only.
    /// </remarks>
    public static JsonSerializerOptions SerializerOptions => JsonConstants.DefaultInternalJsonOptions;
}
