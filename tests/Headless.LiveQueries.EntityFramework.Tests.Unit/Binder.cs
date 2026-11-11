// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// The base of a hierarchy stored in a table per type. The foreign key to the shelf is declared here, so the
/// database deletes a binder's row in this table with its shelf, and with it the row a derived type keeps in its
/// own table.
/// </summary>
internal class Binder
{
    public int Id { get; set; }

    public int ShelfId { get; set; }

    public Shelf? Shelf { get; set; }
}
