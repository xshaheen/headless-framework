// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.CompilerServices;
using System.Xml;

// ReSharper disable UnusedMember.Global
#pragma warning disable IDE0130 // Namespace does not match folder structure
// ReSharper disable once CheckNamespace
namespace Headless.Generator.Primitives;

/// <summary>
/// Provides extension methods for XmlReader and XmlWriter to simplify reading and writing of certain data types.
/// </summary>
public static class XmlReaderExtensions
{
    /// <summary>
    /// Reads the content of the current element as a <see cref="byte" /> object.
    /// </summary>
    /// <param name="reader">The XmlReader instance.</param>
    /// <returns>A byte object representing the value read from the element.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T ReadElementContentAs<T>(this XmlReader reader)
        where T : IParsable<T>
    {
        return T.Parse(reader.ReadElementContentAsString(), CultureInfo.InvariantCulture);
    }
}
