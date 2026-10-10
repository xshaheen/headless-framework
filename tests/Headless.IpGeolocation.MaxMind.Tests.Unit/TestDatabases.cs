// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Formats.Tar;
using System.IO.Compression;
using System.Net;

namespace Tests;

/// <summary>MaxMind's public test databases and the records the tests rely on.</summary>
internal static class TestDatabases
{
    public const string CityEdition = "GeoLite2-City";
    public const string CountryEdition = "GeoLite2-Country";
    public const string AsnEdition = "GeoLite2-ASN";

    /// <summary>Boxford, West Berkshire, GB in the City test database.</summary>
    public static readonly IPAddress BoxfordAddress = IPAddress.Parse("2.125.160.217");

    /// <summary>AS1221 Telstra in the ASN test database; absent from the City one.</summary>
    public static readonly IPAddress TelstraAddress = IPAddress.Parse("1.128.0.1");

    public static readonly IPAddress PrivateAddress = IPAddress.Parse("10.0.0.1");

    public static string AssetPath(string edition) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", edition + "-Test.mmdb");

    /// <summary>Copies the test database for <paramref name="edition" /> into <paramref name="directory" /> under its edition name.</summary>
    public static string Install(string directory, string edition)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, edition + ".mmdb");
        File.Copy(AssetPath(edition), path, overwrite: true);

        return path;
    }

    /// <summary>Packs a database the way MaxMind ships it: a gzipped tar with a dated folder.</summary>
    public static byte[] Archive(string edition, byte[]? database = null, string? entryName = null)
    {
        using var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, entryName ?? $"{edition}_20261010/{edition}.mmdb")
            {
                DataStream = new MemoryStream(database ?? File.ReadAllBytes(AssetPath(edition))),
            };
            tar.WriteEntry(entry);
        }

        return output.ToArray();
    }
}
