// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using System.Runtime.InteropServices;

namespace Headless.Sql;

#pragma warning disable CA1720 // Fix would make it worse: these members name SQL storage widths, and a name other than Int16/32/64 would hide which width each maps to.
/// <summary>The kind of a portable column type.</summary>
[PublicAPI]
public enum SqlColumnKind
{
    KeyText = 0,
    Text = 1,
    Int16 = 2,
    Int32 = 3,
    Int64 = 4,
    Timestamp = 5,
    Binary = 6,

    /// <summary>A 16-byte identifier.</summary>
    Guid = 7,

    /// <summary>A boolean.</summary>
    Boolean = 8,

    /// <summary>A JSON document.</summary>
    Json = 9,
}
