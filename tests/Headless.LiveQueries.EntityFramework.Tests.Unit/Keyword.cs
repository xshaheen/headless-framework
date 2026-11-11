// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>A row many sheets carry, linked to them through a join table EF Core maps without an entity class.</summary>
internal sealed class Keyword
{
    public int Id { get; set; }

    public List<Sheet> Sheets { get; } = [];
}
