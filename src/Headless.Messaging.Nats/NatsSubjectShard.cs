// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Nats;

internal static class NatsSubjectShard
{
    public static string? Validate(string? shard)
    {
        if (shard is null)
        {
            return null;
        }

        foreach (var character in shard.AsSpan())
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || character is '.' or '*' or '>')
            {
                throw new InvalidOperationException(
                    "NATS SubjectShard must be a single safe subject token and cannot contain '.', '*', '>', whitespace, or control characters."
                );
            }
        }

        return shard;
    }
}
