// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus;

/// <summary>Where an upload stands between its last byte and its completion handler, as an <see cref="ITusHandoffStore"/> reports it.</summary>
[PublicAPI]
public enum TusHandoffState
{
    /// <summary>The upload does not hold all of its bytes yet, or does not exist.</summary>
    NotComplete = 0,

    /// <summary>The upload holds all of its bytes and was not handed off: its completion handler is still owed.</summary>
    Pending = 1,

    /// <summary>The upload was handed off: its completion handler returned, and it is the application's from then on.</summary>
    HandedOff = 2,
}
