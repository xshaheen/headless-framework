// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Primitives;

[PublicAPI]
public sealed record OperationDescriptor(string Code, string Href, string Method, string IdempotentKey);
