// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Primitives;

namespace Headless.Text;

[PublicAPI]
public sealed class FormattedStringExtractionResult
{
    private readonly List<NameValue> _matches = [];

    internal FormattedStringExtractionResult(bool isMatch)
    {
        IsMatch = isMatch;
    }

    /// <summary>Is fully matched.</summary>
    public bool IsMatch { get; internal set; }

    /// <summary>List of matched dynamic values.</summary>
    public IReadOnlyList<NameValue> Matches => _matches;

    internal void AddMatch(NameValue match)
    {
        _matches.Add(match);
    }
}
