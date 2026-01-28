// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Core;

/// <summary>Contains methods for creating sequential GUID values.</summary>
public static class SequentialGuid
{
    private static long _counter = DateTime.UtcNow.Ticks;

    /// <summary>
    /// The sequential portion of the GUID should be located at the end of the Data4 block.
    /// Used by SqlServer.
    /// </summary>
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

    /// <summary>
    /// The GUID should be sequential when formatted using the <see cref="Guid.ToString()" /> method.
    /// Used by MySql and PostgreSql.
    /// </summary>
    public static Guid NextSequentialAsString()
    {
        var guidBytes = _GetNextSequentialAsBinaryBytes();

        // If formatting as a string, we have to compensate for the fact that .NET regards the Data1
        // and Data2 block as an Int32 and an Int16, respectively.
        // That means that it switches the order on little-endian systems. So again, we have to reverse.
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(guidBytes, 0, 4);
            Array.Reverse(guidBytes, 4, 2);
        }

        return new Guid(guidBytes);
    }

    /// <summary>
    /// The GUID should be sequential when formatted using the <see cref="Guid.ToByteArray()" /> method.
    /// Used by Oracle.
    /// </summary>
    public static Guid NextSequentialAsBinary()
    {
        var guidBytes = _GetNextSequentialAsBinaryBytes();

        return new Guid(guidBytes);
    }

    private static byte[] _GetNextSequentialAsBinaryBytes()
    {
        // We start with 16 bytes of cryptographically strong random data.
        var randomBytes = Guid.NewGuid().ToByteArray();

        // Now we have the random basis for our GUID. Next, we need to
        // create the six-byte block which will be our timestamp.

        // We start with the number of milliseconds that have elapsed since
        // DateTime.MinValue.  This will form the timestamp.  There's no use
        // being more specific than milliseconds, since DateTime.Now has
        // limited resolution.

        // Using millisecond resolution for our 48-bit timestamp gives us
        // about 5900 years before the timestamp overflows and cycles.
        // Hopefully this should be sufficient for most purposes. :)
        var timestamp = DateTime.UtcNow.Ticks / 10000L;

        // Then get the bytes
        var timestampBytes = BitConverter.GetBytes(timestamp);

        // Since we're converting from an Int64, we have to reverse on little-endian systems.
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(timestampBytes);
        }

        var guidBytes = new byte[16];

        // For string and byte-array version, we copy the timestamp first, followed by the random data.
        Buffer.BlockCopy(timestampBytes, 2, guidBytes, 0, 6);
        Buffer.BlockCopy(randomBytes, 0, guidBytes, 6, 10);

        return guidBytes;
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
