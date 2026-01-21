using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Messaging.Internal;

namespace Tests;

public class HelperTest
{
    [Fact]
    public void IsControllerTest()
    {
        // given
        var typeInfo = typeof(HomeController).GetTypeInfo();

        // when
        var result = Helper.IsController(typeInfo);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void IsControllerAbstractTest()
    {
        // given
        var typeInfo = typeof(AbstractController).GetTypeInfo();

        // when
        var result = Helper.IsController(typeInfo);

        // then
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(decimal))]
    [InlineData(typeof(DateTime))]
    [InlineData(typeof(DateTimeOffset))]
    [InlineData(typeof(Guid))]
    [InlineData(typeof(TimeSpan))]
    [InlineData(typeof(Uri))]
    public void IsSimpleTypeTest(Type type)
    {
        // when
        var result = Helper.IsComplexType(type);

        // then
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(HomeController))]
    [InlineData(typeof(Exception))]
    public void IsComplexTypeTest(Type type)
    {
        // when
        var result = Helper.IsComplexType(type);

        // then
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("192.168.255.255")]
    public void should_return_true_when_ip_is_private(string ipAddress)
    {
        Helper.IsInnerIp(ipAddress).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("172.15.255.255")]
    [InlineData("172.32.0.0")]
    [InlineData("192.167.1.1")]
    [InlineData("11.0.0.0")]
    public void should_return_false_when_ip_is_public(string ipAddress)
    {
        Helper.IsInnerIp(ipAddress).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("999.999.999.999")]
    [InlineData("10.0.0")]
    [InlineData("10.0.0.0.0")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    public void should_return_false_when_ip_is_invalid(string ipAddress)
    {
        Helper.IsInnerIp(ipAddress).Should().BeFalse();
    }

    [Theory]
    [InlineData("user.*", "user.created")]
    [InlineData("user.*", "user.updated")]
    [InlineData("user.#", "user.created.v1")]
    [InlineData("user.#", "user.updated.v2")]
    [InlineData("literal", "literal")]
    public void should_convert_wildcard_to_regex_correctly(string wildcard, string testString)
    {
        // when
        var pattern = Helper.WildcardToRegex(wildcard);
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // then
        regex.IsMatch(testString).Should().BeTrue();
    }

    [Fact]
    public void should_reject_wildcard_exceeding_max_length()
    {
        // given
        var longWildcard = new string('a', 201);

        // when
        var act = () => Helper.WildcardToRegex(longWildcard);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("Topic pattern exceeds maximum length of 200 characters*");
    }

    [Fact]
    public void should_reject_wildcard_with_too_many_wildcards()
    {
        // given
        var manyWildcards = string.Join(".", Enumerable.Repeat("*", 11));

        // when
        var act = () => Helper.WildcardToRegex(manyWildcards);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("Topic pattern contains too many wildcards*");
    }

    [Theory]
    [InlineData("a*")]
    [InlineData("a#")]
    [InlineData("*")]
    [InlineData("#")]
    public void should_use_possessive_quantifiers_to_prevent_redos(string wildcard)
    {
        // when
        var pattern = Helper.WildcardToRegex(wildcard);

        // then
        // Possessive quantifiers use atomic groups (?>...) which prevent backtracking entirely
        pattern.Should().Contain("(?>", "possessive quantifiers (atomic groups) eliminate ReDoS attacks");
    }

    [Fact]
    public void should_complete_regex_matching_within_timeout()
    {
        // given - worst-case input designed to exploit backtracking
        var pattern = Helper.WildcardToRegex("user.*.created");
        var maliciousInput = "user." + new string('a', 100) + ".created";
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // when
        var act = () => regex.IsMatch(maliciousInput);

        // then - should not timeout
        act.Should().NotThrow<RegexMatchTimeoutException>();
    }

    [Fact]
    public void should_handle_pathological_input_instantly_without_backtracking()
    {
        // given - pathological input designed to trigger catastrophic backtracking
        // With non-greedy (+?), this would backtrack for seconds
        // With possessive (?>+), this fails instantly (no backtracking)
        var pattern = Helper.WildcardToRegex("foo.*.bar");
        var pathologicalInput = "foo." + new string('a', 1000); // No 'bar' at end
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // when - measure time to ensure instant failure
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var matches = regex.IsMatch(pathologicalInput);
        sw.Stop();

        // then - possessive quantifier should fail instantly (< 100ms)
        // Non-greedy would take ~1 second (timeout)
        matches.Should().BeFalse();
        sw.ElapsedMilliseconds.Should().BeLessThan(100, "possessive quantifiers prevent backtracking");
    }

    [Fact]
    public void should_escape_regex_special_characters()
    {
        // given
        var wildcardWithSpecialChars = "user.order[123].items";

        // when
        var pattern = Helper.WildcardToRegex(wildcardWithSpecialChars);
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // then
        regex.IsMatch("user.order[123].items").Should().BeTrue();
        regex.IsMatch("user.order123.items").Should().BeFalse();
    }
}

public class HomeController;

public abstract class AbstractController;
