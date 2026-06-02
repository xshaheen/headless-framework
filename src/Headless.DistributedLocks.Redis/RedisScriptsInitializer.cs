// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Hosting.Initialization;
using Headless.Redis;

namespace Headless.DistributedLocks.Redis;

/// <summary>
/// Loads a fixed set of Lua script definitions into the keyed <see cref="HeadlessRedisScriptsLoader"/>
/// on host startup. One instance is registered per lock family (mutex, reader-writer, semaphore),
/// each carrying its own definition list, replacing the three near-identical initializer types.
/// </summary>
internal sealed class RedisScriptsInitializer(
    HeadlessRedisScriptsLoader loader,
    IReadOnlyList<RedisScriptDefinition> definitions
) : HostedInitializer
{
    private readonly HeadlessRedisScriptsLoader _loader = Argument.IsNotNull(loader);
    private readonly IReadOnlyList<RedisScriptDefinition> _definitions = Argument.IsNotNull(definitions);

    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _loader.LoadAsync(_definitions, cancellationToken).ConfigureAwait(false);
    }
}
