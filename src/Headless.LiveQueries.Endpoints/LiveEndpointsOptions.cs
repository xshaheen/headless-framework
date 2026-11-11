// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.LiveQueries;

/// <summary>Settings of live endpoints: the heartbeat of their streams and the limits that bound their cost.</summary>
[PublicAPI]
public sealed class LiveEndpointsOptions
{
    /// <summary>
    /// The service key of the <see cref="TimeProvider" /> that <see cref="MinRerunInterval" /> is measured on. It
    /// defaults to <see cref="TimeProvider.System" /> and is separate from the application clock, so a host that
    /// registers a fake application clock and never advances it still updates its streams. A test steps through
    /// the interval by registering its own clock:
    /// <c>services.AddKeyedSingleton&lt;TimeProvider&gt;(LiveEndpointsOptions.RerunClockServiceKey, fakeClock)</c>.
    /// </summary>
    public const string RerunClockServiceKey = "Headless:LiveQueries:RerunClock";

    /// <summary>
    /// Gets or sets how long a stream stays silent before the transport writes a heartbeat. The heartbeat keeps
    /// proxies and load balancers from closing an idle connection, most of which do so after 30 to 60 seconds, and
    /// lets the server notice a client that went away. It waits real time. Default: 20 seconds. At most 1 hour.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets or sets how long a server-sent events client waits before it reconnects after the stream ends, sent as
    /// the stream's <c>retry:</c> field, and the <c>Retry-After</c> of a refused stream. A few seconds spread the
    /// reconnects of every client after a deployment instead of landing them on the first instant the host is
    /// back. Default: 3 seconds.
    /// </summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Gets or sets how many streams one user may hold at a time. A stream past the limit is refused with 429. An
    /// anonymous stream counts against the limit of its remote address. A screen holds a handful of live queries
    /// and a user a few tabs, and an HTTP/2 connection carries 100 concurrent streams by default, so the default
    /// leaves room for ordinary use while one client cannot hold a large share of <see cref="MaxStreams" />.
    /// Default: 50.
    /// </summary>
    public int MaxStreamsPerUser { get; set; } = 50;

    /// <summary>
    /// Gets or sets how many streams this host holds at a time, across all users. A stream past the limit is
    /// refused with 429. Each open stream keeps a connection or HTTP/2 stream and its last result's hash, and every
    /// announced change is compared with every open stream, so the count bounds both memory and the cost of a
    /// write. Default: 10,000.
    /// </summary>
    public int MaxStreams { get; set; } = 10_000;

    /// <summary>
    /// Gets or sets how many re-runs execute at a time across the host. One write can signal every stream that
    /// read the written data, and each re-run usually takes a database connection; without a bound those re-runs
    /// would start together and starve the requests that write. A signalled stream past the limit waits for its
    /// turn. The first run of a stream is not counted: it is one request, as an ordinary GET is. The default stays
    /// well under the 100 connections the common database pools open. Default: 32.
    /// </summary>
    public int MaxConcurrentReruns { get; set; } = 32;

    /// <summary>
    /// Gets or sets the least time between the starts of two runs of one stream. Changes announced inside the
    /// interval are answered by one re-run when it ends, so a burst of writes costs a stream one run and not one
    /// per write. The default is below what a person perceives as a delay and caps a stream at ten runs a second.
    /// <see cref="TimeSpan.Zero" /> turns the wait off. Default: 100 milliseconds.
    /// </summary>
    public TimeSpan MinRerunInterval { get; set; } = TimeSpan.FromMilliseconds(100);
}

internal sealed class LiveEndpointsOptionsValidator : AbstractValidator<LiveEndpointsOptions>
{
    public LiveEndpointsOptionsValidator()
    {
        RuleFor(x => x.HeartbeatInterval).GreaterThan(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.ReconnectDelay).GreaterThanOrEqualTo(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
        RuleFor(x => x.MaxStreamsPerUser).GreaterThan(0);
        RuleFor(x => x.MaxStreams).GreaterThan(0);
        RuleFor(x => x.MaxConcurrentReruns).GreaterThan(0);
        RuleFor(x => x.MinRerunInterval).GreaterThanOrEqualTo(TimeSpan.Zero).LessThanOrEqualTo(TimeSpan.FromHours(1));
    }
}
