// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Headless.Checks;

namespace Headless.PushNotifications.Apns.Internal;

/// <summary>An APNs provider token (ES256 JWT) and the mint generation that identifies it.</summary>
/// <param name="Value">The signed JWT sent as the <c>authorization: bearer</c> value.</param>
/// <param name="Generation">
/// Increases with every mint for one key identity; a caller passes it back to invalidate exactly the token it used.
/// </param>
/// <param name="MintedAt">The instant written into the <c>iat</c> claim.</param>
internal sealed record ApnsProviderToken(string Value, long Generation, DateTimeOffset MintedAt);
