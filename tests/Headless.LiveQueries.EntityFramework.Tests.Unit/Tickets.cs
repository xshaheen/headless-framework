// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// The entity of the row-matching scenarios: a generated key, columns fixed at insert of every type a filter may
/// compare, and columns that stay changeable.
/// </summary>
internal sealed class Ticket
{
    public int Id { get; set; }

    public int OwnerId
    {
        get
        {
            if (FailNextOwnerRead)
            {
                FailNextOwnerRead = false;

                throw new InvalidOperationException("Simulated failure reading a property of a saved entity.");
            }

            return field;
        }
        set;
    }

    /// <summary>Makes the next read of <see cref="OwnerId" /> throw, once.</summary>
    public bool FailNextOwnerRead { get; set; }

    public TicketKind Kind { get; set; }

    public Guid Tenant { get; set; }

    public bool Urgent { get; set; }

    public int? Region { get; set; }

    /// <summary>Fixed at insert like the others, and of a type no filter is matched on.</summary>
    public string Code { get; set; } = "";

    /// <summary>Changeable after insert, so a filter on it says nothing about a saved row.</summary>
    public int Priority { get; set; }

    public string Title { get; set; } = "";

    public List<Comment> Comments { get; } = [];
}

internal enum TicketKind
{
    Bug,
    Feature,
}

internal sealed class Comment
{
    public int Id { get; set; }

    public int TicketId { get; set; }

    public Ticket? Ticket { get; set; }

    public string Text { get; set; } = "";
}

/// <summary>Loads its shelf with every query, so one folder's result holds a row its own filter does not name.</summary>
internal sealed class Folder
{
    public int Id { get; set; }

    public int ShelfId { get; set; }

    public Shelf? Shelf { get; set; }

    public string Name { get; set; } = "";
}

internal sealed class Shelf
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>A row the database deletes with its folder, and so with its folder's shelf.</summary>
internal sealed class Sheet
{
    public int Id { get; set; }

    public int FolderId { get; set; }

    public Folder? Folder { get; set; }
}

/// <summary>A row the database detaches from its shelf, by clearing the column, when the shelf is deleted.</summary>
internal sealed class Label
{
    public int Id { get; set; }

    public int? ShelfId { get; set; }

    public Shelf? Shelf { get; set; }
}

/// <summary>Hidden from every query while another invoice, of any owner, voids it.</summary>
internal sealed class Invoice
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    /// <summary>The key of the invoice this one voids, if any.</summary>
    public int? Voids { get; set; }
}

/// <summary>Hidden from every query by a column of its own row.</summary>
internal sealed class Memo
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public bool Hidden { get; set; }
}

/// <summary>The tickets table as a second model maps it, where the owner is an ordinary changeable column.</summary>
internal sealed class TicketRow
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public int Priority { get; set; }
}

/// <summary>Has a key of two parts, one of them text, which a row is not matched on.</summary>
internal sealed class Badge
{
    public int Shelf { get; set; }

    public string Code { get; set; } = "";

    public int OwnerId { get; set; }

    public string Note { get; set; } = "";
}
