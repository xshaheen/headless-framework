// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Domain;

public interface IHasETag
{
    /// <summary>Raw version of the entity used for concurrency control</summary>
    byte[]? ETag { get; set; }
}
