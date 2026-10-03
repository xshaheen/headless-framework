// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.SourceGenerators;

namespace Headless.Messaging.SourceGenerator.Models;

/// <summary>One request a consumer class answers, and the one response type it answers with.</summary>
/// <param name="RequestTypeName">The fully qualified <c>TRequest</c>.</param>
/// <param name="ResponseTypeName">The fully qualified <c>TResponse</c>.</param>
internal sealed record ResponderModel(string RequestTypeName, string ResponseTypeName);
