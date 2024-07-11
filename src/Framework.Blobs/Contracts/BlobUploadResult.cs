#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Blobs;

public sealed record BlobUploadResult(string SavedName, string DisplayName, long Size);
