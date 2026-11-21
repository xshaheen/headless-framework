// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Testing.Testcontainers;

/// <summary>What one probe of a container's published port read back on the test host.</summary>
public enum PublishedPortReply
{
    /// <summary>Nothing usable answered yet: closed, reset, or a server that is still starting.</summary>
    NotYet,

    /// <summary>The container's own server answered.</summary>
    Container,

    /// <summary>Another listener answered, so the port is lost to the container for its whole run.</summary>
    AnotherListener,
}
