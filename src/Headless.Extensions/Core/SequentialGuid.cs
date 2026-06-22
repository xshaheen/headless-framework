// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>
/// Creates GUIDs that stay sequential in SQL Server's <c>uniqueidentifier</c> sort order. For byte-ordered
/// stores (PostgreSQL, MySQL binary, Oracle) and general use, prefer <see cref="Guid.CreateVersion7()"/> instead —
/// see <c>SequentialGuidType</c>.
/// </summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    /// <summary>
    /// The sequential portion of the GUID is located in the last 8 bytes (the Data4 / node block), which is the
    /// block SQL Server treats as most significant when sorting <c>uniqueidentifier</c>. Byte-for-byte the same
    /// strategy as EF Core's <c>SequentialGuidValueGenerator</c>. Use for SQL Server clustered/primary keys.
    /// </summary>
    /// <returns>A new <see cref="Guid"/> whose trailing bytes increase monotonically across calls, keeping values sequential in SQL Server's sort order.</returns>
    public static Guid NextSequentialAtEnd()
    {
        var guidBytes = Guid.NewGuid().ToByteArray();
        var counterBytes = BitConverter.GetBytes(Interlocked.Increment(ref _counter));

        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        guidBytes[08] = counterBytes[1];
        guidBytes[09] = counterBytes[0];
        guidBytes[10] = counterBytes[7];
        guidBytes[11] = counterBytes[6];
        guidBytes[12] = counterBytes[5];
        guidBytes[13] = counterBytes[4];
        guidBytes[14] = counterBytes[3];
        guidBytes[15] = counterBytes[2];

        return new Guid(guidBytes);
    }
}

/*
 * This code is taken from:
 *
 * https://www.codeproject.com/Articles/388157/GUIDs-as-fast-primary-keys-under-multiple-database
 * https://github.com/jhtodd/SequentialGuid/blob/master/SequentialGuid/Classes/SequentialGuid.cs
 * https://github.com/nhibernate/nhibernate-core/blob/master/src/NHibernate/Id/GuidCombGenerator.cs
 * https://github.com/dotnet/efcore/blob/main/src/EFCore/ValueGeneration/SequentialGuidValueGenerator.cs
 */
