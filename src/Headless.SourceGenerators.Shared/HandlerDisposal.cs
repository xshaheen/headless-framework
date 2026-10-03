// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Headless.SourceGenerators;

/// <summary>Which dispose pattern a handler class implements, decided at build time so the generated call stays typed.</summary>
internal enum HandlerDisposal
{
    None,
    Sync,
    Async,
}
