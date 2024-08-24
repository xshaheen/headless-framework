using Amazon.S3;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorageSettings
{
    public bool UseChunkEncoding { get; set; } = true;

    public S3CannedACL? CannedAcl { get; set; }

    public ILoggerFactory? LoggerFactory { get; init; }
}
