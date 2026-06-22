// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Nats;

internal static class NatsSubjectShard
{
    // A single shard token must stay well within the NATS subject length budget; cap it so a
    // degenerate selector value fails fast instead of producing an oversized subject.
    private const int _MaxShardLength = 256;

    public static string? Validate(string? shard)
    {
        if (shard is null)
        {
            return null;
        }

        if (shard.Length == 0)
        {
            throw new InvalidOperationException(
                "NATS SubjectShard must be a non-empty token. Return null from the shard selector to skip sharding."
            );
        }

        if (shard.Length > _MaxShardLength)
        {
            throw new InvalidOperationException(
                $"NATS SubjectShard exceeds the maximum length of {_MaxShardLength} characters."
            );
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
