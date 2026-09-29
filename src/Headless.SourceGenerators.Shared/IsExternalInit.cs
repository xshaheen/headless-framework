// Copyright (c) Mahmoud Shaheen. All rights reserved.

#if NETSTANDARD2_0
// Generators target netstandard2.0, which lacks this marker type; records and init accessors need it to compile.
// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit;
#endif
