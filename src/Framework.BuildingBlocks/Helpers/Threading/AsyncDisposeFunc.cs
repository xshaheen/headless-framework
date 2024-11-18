// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Framework.BuildingBlocks.Helpers.Threading;

/// <summary>This class can be used to provide an action when  DisposeAsync method is called.</summary>
/// <remarks>Creates a new <see cref="AsyncDisposeFunc"/> object.</remarks>
/// <param name="func">func to be executed when this object is DisposeAsync.</param>
[PublicAPI]
public sealed class AsyncDisposeFunc(Func<Task> func) : IAsyncDisposable
{
    private readonly Func<Task> _func = Argument.IsNotNull(func);

    public async ValueTask DisposeAsync() => await _func();
}
