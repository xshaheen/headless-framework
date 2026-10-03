// Copyright (c) Mahmoud Shaheen. All rights reserved.
// Polyfills for C# 9+ features in netstandard2.0

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit;
