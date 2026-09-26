// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the application DbContext, invoice entity, and variables the sequences guide's examples assume.

global using static SequencesAmbient;
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);

public sealed class Invoice
{
    public long Number { get; set; }
}

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class SequencesAmbient
{
    public static string connectionString => null!;

    public static ISequenceGenerator sequences => null!;

    public static IUnitOfWorkFactory unitOfWorkFactory => null!;

    public static AppDbContext db => null!;

    public static Invoice invoice => null!;
}
