// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>One server-sent event as it arrived.</summary>
/// <param name="Name">The <c>event:</c> field.</param>
/// <param name="Id">The <c>id:</c> field.</param>
/// <param name="Data">The <c>data:</c> lines, one entry for each.</param>
internal sealed record ServerSentEvent(string? Name, string? Id, IReadOnlyList<string> Data)
{
    public JsonElement Json => JsonElement.Parse(string.Join('\n', Data));
}
