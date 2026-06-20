// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>Exposes whether an object has already been disposed.</summary>
public interface IHasIsDisposed
{
    /// <summary>Gets a value indicating whether this instance has been disposed.</summary>
    bool IsDisposed { get; }
}
