// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>One <c>IRequestClient.RequestAsync&lt;TRequest, TResponse&gt;</c> call with concrete type arguments.</summary>
/// <param name="RequestTypeName">The fully qualified <c>TRequest</c>.</param>
/// <param name="ResponseTypeName">The fully qualified <c>TResponse</c>.</param>
/// <param name="Location">The method name and its type arguments, where a mismatch is reported.</param>
internal sealed record RequestCallModel(string RequestTypeName, string ResponseTypeName, LocationInfo? Location);
