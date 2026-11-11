// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api;

/// <summary>
/// Endpoint metadata for a live endpoint: besides its ordinary single response, it answers a request that asks for a
/// stream with its result now and again whenever the result changes. The feature that makes an endpoint live attaches
/// it, so API documentation can describe the stream without depending on that feature.
/// </summary>
/// <remarks>
/// <para>
/// It marks the endpoint only: nothing reads it at run time, so attaching it by hand makes no endpoint live.
/// Headless.OpenApi renders it as the <c>x-headless-live</c> operation extension and as the stream media types of the
/// operation's success response.
/// </para>
/// <para>
/// It is a class rather than a flag on <see cref="HttpContractMetadata"/>, which lists headers and responses a
/// feature adds: a live endpoint adds none of those, it serves the same success body in another representation.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LiveUpdatesMetadata;
