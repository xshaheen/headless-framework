// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Models;

public sealed class Product
{
    public string Name { get; set; } = default!;

    public decimal Price { get; set; }

    public string Category { get; set; } = default!;

    public int Stock { get; set; }

    public Color Color { get; set; }

    public bool Available { get; set; }
}

public enum Color
{
    Default,
    Red,
    Green,
    Black,
    White,
}
