// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>The outcome of one run of a live query, as the hub needs it.</summary>
/// <param name="Payload">The result as UTF-8 JSON, or the failure body when <paramref name="Failed" /> is set.</param>
/// <param name="Failed">Whether the run did not succeed; the hub then sends the payload and ends the subscription.</param>
/// <param name="Dependencies">What the run read, taken from the recorder the runner began for it.</param>
internal readonly record struct LiveRunResult(ReadOnlyMemory<byte> Payload, bool Failed, LiveDependencies Dependencies);
