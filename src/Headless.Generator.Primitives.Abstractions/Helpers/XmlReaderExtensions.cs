// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Generator.Primitives;

/// <summary>
/// Provides extension methods for XmlReader and XmlWriter to simplify reading and writing of certain data types.
/// </summary>
/// <remarks>This type is generator-output plumbing; it must stay public for emitted code but is not intended for direct use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class XmlReaderExtensions
{
    /// <summary>
    /// Reads the text content of the current element and parses it as <typeparamref name="T"/>
    /// using the invariant culture.
    /// </summary>
    /// <typeparam name="T">The target type, which must implement <c>IParsable&lt;T&gt;</c>.</typeparam>
    /// <param name="reader">The XmlReader positioned on the element whose content is to be read.</param>
    /// <returns>The parsed value of type <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadElementContentAs<T>(this XmlReader reader)
        where T : IParsable<T>
    {
        return T.Parse(reader.ReadElementContentAsString(), CultureInfo.InvariantCulture);
    }
}
