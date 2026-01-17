// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace DotNetCore.CAP.Dashboard.NodeDiscovery;

public class Node
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public required string Address { get; set; }

    public required int Port { get; set; }

    public required string Tags { get; set; }

    public string? Latency { get; set; }
}
