// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using StackExchange.Redis;

namespace Headless.Caching;

[PublicAPI]
public sealed class RedisCacheOptions : CacheOptions
{
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    /// <summary>The behaviour required when performing read operations from cache.</summary>
    public CommandFlags ReadMode { get; set; } = CommandFlags.None;

    /// <summary>
    /// Maximum number of tag-hash members processed per Lua call during <c>RemoveByTagAsync</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each <c>RemoveByTagAsync</c> call fetches this many members from the tag hash, removes
    /// matched entries, and then repeats until the hash is fully drained. Capping the per-call
    /// work keeps each Lua invocation bounded and prevents long-running scripts on tags with
    /// very large membership sets.
    /// </para>
    /// <para>
    /// The default of 50 is intentionally conservative: each member requires a <c>GETRANGE</c>
    /// header read to verify the version stamp, so per-EVALSHA latency grows linearly with
    /// batch size. Smaller batches keep individual script calls short; the C# loop ensures
    /// completeness regardless of tag cardinality.
    /// </para>
    /// <para>The total count returned by <c>RemoveByTagAsync</c> is the sum across all batches.</para>
    /// </remarks>
    public int MaxMembersPerTagRemoval { get; set; } = 50;
}

internal sealed class RedisCacheOptionsValidator : AbstractValidator<RedisCacheOptions>
{
    public RedisCacheOptionsValidator()
    {
        RuleFor(x => x.KeyPrefix).NotNull();
        RuleFor(x => x.ConnectionMultiplexer).NotNull();
        RuleFor(x => x.ReadMode).IsInEnum();
        RuleFor(x => x.MaxMembersPerTagRemoval).GreaterThan(0);
    }
}
