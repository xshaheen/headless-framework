// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Blobs;

internal enum BlobSignedUrlAccess : byte
{
    Download = 1,
    Upload = 2,
}
