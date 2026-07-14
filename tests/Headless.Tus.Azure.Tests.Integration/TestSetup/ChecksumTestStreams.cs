// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.TestSetup;

internal static class ChecksumTestStreams
{
    /// <summary>
    /// Wraps <paramref name="content"/> in tusdotnet's internal <c>ChecksumAwareStream</c> (the only
    /// thing the store's <c>GetUploadChecksumInfo()</c> recognizes) so <c>AppendDataAsync</c> runs the
    /// checksum-header staging path. The type is internal to tusdotnet, hence reflection.
    /// </summary>
    public static Stream CreateChecksumAware(byte[] content, byte[] digest, string algorithm)
    {
        var checksum = new tusdotnet.Models.Checksum($"{algorithm} {Convert.ToBase64String(digest)}");
        var streamType = typeof(tusdotnet.Models.Checksum).Assembly.GetType(
            "tusdotnet.Models.Streams.ChecksumAwareStream",
            throwOnError: true
        )!;

        return (Stream)Activator.CreateInstance(streamType, new MemoryStream(content), checksum)!;
    }
}
