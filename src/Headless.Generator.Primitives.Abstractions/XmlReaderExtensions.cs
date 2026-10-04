// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Xml;

// ReSharper disable UnusedMember.Global
namespace Headless.Generator.Primitives;

/// <summary>
/// Provides extension methods for <see cref="XmlReader"/> to simplify parsing element content.
/// </summary>
/// <remarks>Internal generator-emitted plumbing. Kept public for generated code execution.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class XmlReaderExtensions
{
    /// <summary>
    /// Reads the text content of the current element and parses it as <typeparamref name="T"/>
    /// using the invariant culture.
    /// </summary>
    /// <typeparam name="T">The target type that implements <see cref="IParsable{T}"/>.</typeparam>
    /// <param name="reader">The XML reader positioned on the target element.</param>
    /// <returns>The parsed value of type <typeparamref name="T"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadElementContentAs<T>(this XmlReader reader)
        where T : IParsable<T>
    {
        return T.Parse(reader.ReadElementContentAsString(), CultureInfo.InvariantCulture);
    }
}
