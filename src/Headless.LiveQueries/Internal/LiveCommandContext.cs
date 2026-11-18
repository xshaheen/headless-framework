// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.LiveQueries.Internal;

/// <summary>
/// The command a client tagged, for as long as its request runs. The host enters it before the command's handler,
/// so it flows down to wherever the command's writes are announced, and the hub reads it there. That keeps the
/// packages that announce writes free of any knowledge of HTTP.
/// </summary>
/// <remarks>
/// The ambient value only flows down. What the hub has to tell the host, that a write was announced, is written to
/// this object, which the host kept a reference to.
/// </remarks>
internal sealed class LiveCommandContext
{
    private static readonly AsyncLocal<LiveCommandContext?> _Current = new();

    private volatile bool _announced;
    private volatile bool _ended;

    private LiveCommandContext(LiveSubscriber sender, string commandId)
    {
        Sender = sender;
        CommandId = commandId;
    }

    /// <summary>Gets the command this code runs for, or <see langword="null" /> outside a tagged command.</summary>
    public static LiveCommandContext? Current => _Current.Value;

    public LiveSubscriber Sender { get; }

    public string CommandId { get; }

    /// <summary>Gets a value indicating whether a write was announced for the command before it ended.</summary>
    public bool Announced => _announced;

    /// <summary>
    /// Returns the command, or <see langword="null" /> when it has no sender or <paramref name="commandId" /> is
    /// not a token of at most 64 characters. The command then runs untagged.
    /// </summary>
    public static LiveCommandContext? TryCreate(LiveSubscriber? sender, string? commandId)
    {
        return sender is not null && LiveToken.IsValid(commandId) ? new LiveCommandContext(sender, commandId) : null;
    }

    /// <summary>
    /// Makes this the ambient command. Call it synchronously inside the async method that awaits the command's
    /// handler: the runtime then scopes it to that method and the calls it makes, and restores the caller's state
    /// when the method returns.
    /// </summary>
    public void Enter()
    {
        _Current.Value = this;
    }

    /// <summary>
    /// Records that a write is being announced for the command. Returns <see langword="false" /> once the command
    /// has ended, and the write is then acknowledged to nobody.
    /// </summary>
    public bool TryAnnounce()
    {
        if (_ended)
        {
            return false;
        }

        _announced = true;

        return true;
    }

    /// <summary>
    /// Ends the command. Work that the request started and left running, such as a timer, still carries this
    /// object as its ambient command; its later writes are not the command's and must not be reported as such.
    /// </summary>
    public void End()
    {
        _ended = true;
    }
}
