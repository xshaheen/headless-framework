// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Aws;

public sealed class AwsBlobStorageOptions
{
    public bool UseChunkEncoding { get; set; } = true;

    public bool DisablePayloadSigning { get; set; }

    public S3CannedACL? CannedAcl { get; set; } = S3CannedACL.Private;

    public ILoggerFactory? LoggerFactory { get; set; }
}
