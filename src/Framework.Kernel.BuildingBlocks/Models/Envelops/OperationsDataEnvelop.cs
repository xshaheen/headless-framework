// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Framework.Kernel.Primitives;

public sealed record OperationsDataEnvelop<T>(T Data, List<OperationDescriptor>? Operations = null);
