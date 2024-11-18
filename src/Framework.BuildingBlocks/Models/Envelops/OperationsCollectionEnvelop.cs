// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Framework.Primitives;

public sealed record OperationsCollectionEnvelop<T>(
    IReadOnlyCollection<T> Items,
    List<OperationDescriptor>? Operations = null
);
