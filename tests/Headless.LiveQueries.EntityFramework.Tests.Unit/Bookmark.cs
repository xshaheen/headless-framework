// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// A row that refers to a sheet through a foreign key the model cascades on the client only, so EF Core creates
/// the constraint without a delete action.
/// </summary>
internal sealed class Bookmark
{
    public int Id { get; set; }

    public int SheetId { get; set; }

    public Sheet? Sheet { get; set; }
}
