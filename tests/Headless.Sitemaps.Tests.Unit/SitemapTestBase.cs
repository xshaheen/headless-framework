// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Xml.Linq;
using Framework.Testing.Tests;

namespace Tests;

public class SitemapTestBase : TestBase
{
    protected static void AssertEquivalentXml(string result, string expected)
    {
        result = XDocument.Parse(result).ToString();
        expected = XDocument.Parse(expected).ToString();
        result.Should().Be(expected);
    }
}
