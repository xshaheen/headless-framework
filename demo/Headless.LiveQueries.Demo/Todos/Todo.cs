// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Demo;

public sealed class Todo
{
    public Guid Id { get; set; }

    public required string Title { get; set; }

    public bool Done { get; set; }

    /// <summary>Set when the todo is created and never changed; the model refuses an update to it.</summary>
    public int OwnerId { get; set; }
}
