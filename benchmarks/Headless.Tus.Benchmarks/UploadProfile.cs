// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Tus.Benchmarks;

/// <summary>How a client sends one 100 MiB upload.</summary>
public enum UploadProfile
{
    /// <summary>The length is declared at creation and the body arrives in 8 MiB PATCH requests.</summary>
    Chunks8MiB,

    /// <summary>The length is declared at creation and the body arrives in 1 MiB PATCH requests.</summary>
    Chunks1MiB,

    /// <summary>
    /// The upload is created with a deferred length, the body arrives in 8 MiB PATCH requests, and the final PATCH
    /// declares the length.
    /// </summary>
    DeferredLength,
}

/// <summary>The S3 tus store on the server side of an upload.</summary>
public enum TusStoreKind
{
    /// <summary>Headless.Tus.S3.</summary>
    Headless,

    /// <summary>The tusdotnet.Stores.S3 community store.</summary>
    TusdotnetS3,
}

internal static class UploadProfiles
{
    public const int MiB = 1024 * 1024;

    /// <summary>The size of every profile's upload.</summary>
    public const int UploadLength = 100 * MiB;

    /// <summary>The part size both stores are configured with, so neither wins by writing larger parts.</summary>
    public const int PartSize = 8 * MiB;

    public static int ChunkSize(this UploadProfile profile)
    {
        return profile == UploadProfile.Chunks1MiB ? MiB : 8 * MiB;
    }

    public static int PatchCount(this UploadProfile profile)
    {
        var chunk = profile.ChunkSize();

        return (UploadLength + chunk - 1) / chunk;
    }

    /// <summary>The same pseudo-random payload for every run, so both stores upload identical bytes.</summary>
    public static byte[] CreatePayload()
    {
        var payload = GC.AllocateUninitializedArray<byte>(UploadLength);
#pragma warning disable CA5394 // Benchmark payload, not a security value: a fixed seed keeps runs comparable.
        new Random(42).NextBytes(payload);
#pragma warning restore CA5394

        return payload;
    }
}

/// <summary>One store driven through one profile; a benchmark parameter.</summary>
public sealed record UploadCase(TusStoreKind Store, UploadProfile Profile)
{
    /// <summary>Every store and profile pair, supported or not.</summary>
    public static IEnumerable<UploadCase> All =>
        from store in Enum.GetValues<TusStoreKind>()
        from profile in Enum.GetValues<UploadProfile>()
        select new UploadCase(store, profile);

    /// <summary>
    /// Whether the store can finish this upload at all. tusdotnet.Stores.S3 writes each PATCH as its own multipart
    /// part, so S3 refuses to complete an upload sent in PATCHes under the 5 MiB part minimum, and it answers the
    /// first PATCH of a deferred-length upload with 413. The request scenario still runs those pairs and reports the
    /// failure; the timed benchmark leaves them out.
    /// </summary>
    public bool IsSupported =>
        Store != TusStoreKind.TusdotnetS3 || Profile is not (UploadProfile.Chunks1MiB or UploadProfile.DeferredLength);

    // BenchmarkDotNet truncates a parameter value past 20 characters in the summary table, so the profile is abbreviated.
    public override string ToString()
    {
        var profile = Profile switch
        {
            UploadProfile.Chunks8MiB => "8MiB",
            UploadProfile.Chunks1MiB => "1MiB",
            _ => "Deferred",
        };

        return $"{Store}/{profile}";
    }
}
