// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Sockets;
using DotNet.Testcontainers.Containers;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Reads one reply from a fresh connection to a container's published port and names who answered.
/// </summary>
/// <param name="container">The container the port belongs to, for a value only it holds, read through an exec.</param>
/// <param name="containerPort">The container port whose host mapping the connection reached.</param>
/// <param name="stream">The connection, already open.</param>
/// <param name="cancellationToken">Ends the attempt.</param>
public delegate Task<PublishedPortReply> PublishedPortProbe(
    IContainer container,
    int containerPort,
    NetworkStream stream,
    CancellationToken cancellationToken
);
