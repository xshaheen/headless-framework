// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A row that refers to another row of its own table, which the database deletes it with.</summary>
internal sealed class Section
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    public Section? Parent { get; set; }
}
