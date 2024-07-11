#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed record BlobUploadRequest(Stream Stream, string FileName, IDictionary<string, object?>? Metadata = null);
