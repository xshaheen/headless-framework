// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Internal;

/// <summary>
/// The completion handler refused an upload, with the reason to answer the client with. It carries the refusal
/// out of tusdotnet's completion event, which has no way to fail its request, to the endpoint's request delegate,
/// which answers it.
/// </summary>
#pragma warning disable CA1064 // False positive: the endpoint's request delegate catches it and answers the request, so it never leaves this assembly.
internal sealed class TusUploadRejectedException(string message) : Exception(message);
#pragma warning restore CA1064
