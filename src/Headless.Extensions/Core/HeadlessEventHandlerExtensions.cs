// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace System;

/// <summary>Extension methods for <see cref="EventHandler"/>.</summary>
[PublicAPI]
public static class HeadlessEventHandlerExtensions
{
    /// <summary>Invokes the event handler with <see cref="EventArgs.Empty"/> if it is not <see langword="null"/>; does nothing otherwise.</summary>
    /// <param name="eventHandler">The event handler to invoke, or <see langword="null"/> to no-op.</param>
    /// <param name="sender">The source of the event passed to each subscriber.</param>
    public static void InvokeSafely(this EventHandler? eventHandler, object sender)
    {
        eventHandler.InvokeSafely(sender, EventArgs.Empty);
    }

    /// <summary>Invokes the event handler with the given arguments if it is not <see langword="null"/>; does nothing otherwise.</summary>
    /// <param name="eventHandler">The event handler to invoke, or <see langword="null"/> to no-op.</param>
    /// <param name="sender">The source of the event passed to each subscriber.</param>
    /// <param name="e">The event data passed to each subscriber.</param>
    public static void InvokeSafely(this EventHandler? eventHandler, object sender, EventArgs e)
    {
        eventHandler?.Invoke(sender, e);
    }

    /// <summary>Invokes the generic event handler with the given arguments if it is not <see langword="null"/>; does nothing otherwise.</summary>
    /// <typeparam name="TEventArgs">The event data type.</typeparam>
    /// <param name="eventHandler">The event handler to invoke, or <see langword="null"/> to no-op.</param>
    /// <param name="sender">The source of the event passed to each subscriber.</param>
    /// <param name="e">The event data passed to each subscriber.</param>
    public static void InvokeSafely<TEventArgs>(
        this EventHandler<TEventArgs>? eventHandler,
        object sender,
        TEventArgs e
    )
        where TEventArgs : EventArgs
    {
        eventHandler?.Invoke(sender, e);
    }
}
