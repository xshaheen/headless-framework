using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;

namespace Tests;

public sealed class HelperTest
{
    [Fact]
    public void is_controller_test()
    {
        // given
        var typeInfo = typeof(HomeController).GetTypeInfo();

        // when
        var result = ControllerTypeDetector.IsController(typeInfo);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void is_controller_abstract_test()
    {
        // given
        var typeInfo = typeof(AbstractController).GetTypeInfo();

        // when
        var result = ControllerTypeDetector.IsController(typeInfo);

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
    public void is_simple_type_test(Type type)
    {
        // when
        var result = RuntimeTypeInspection.IsComplexType(type);

        // then
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(typeof(HomeController))]
    [InlineData(typeof(Exception))]
    public void is_complex_type_test(Type type)
    {
        // when
        var result = RuntimeTypeInspection.IsComplexType(type);

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
        HostIdentity.IsInnerIp(ipAddress).Should().BeTrue();
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
        HostIdentity.IsInnerIp(ipAddress).Should().BeFalse();
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
        HostIdentity.IsInnerIp(ipAddress).Should().BeFalse();
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
        var pattern = TransportNaming.WildcardToRegex(wildcard);
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // then
        regex.IsMatch(testString).Should().BeTrue();
    }

    [Theory]
    [InlineData("orders.*.#", "orders.created.v1", true)]
    [InlineData("orders.*.#", "orders.updated.v1.beta", true)]
    [InlineData("*.#", "user.created.v1", true)]
    [InlineData("*.#", "user.created", true)]
    [InlineData("orders.*.#", "orders.created", false)]
    [InlineData("orders.*.#", "orders", false)]
    public void should_convert_mixed_wildcard_to_regex_correctly(string wildcard, string testString, bool expected)
    {
        // given — mixed * and # wildcards must each be expanded; an earlier implementation
        // returned after handling * and left # as a literal in the resulting regex,
        // which silently broke any pattern combining the two.
        // when
        var pattern = TransportNaming.WildcardToRegex(wildcard);
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // then
        regex.IsMatch(testString).Should().Be(expected);
    }

    [Fact]
    public void should_reject_wildcard_exceeding_max_length()
    {
        // given
        var longWildcard = new string('a', 201);

        // when
        var act = () => TransportNaming.WildcardToRegex(longWildcard);

        // then
        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("MessageName pattern exceeds maximum length of 200 characters*");
    }

    [Fact]
    public void should_reject_wildcard_with_too_many_wildcards()
    {
        // given
        var manyWildcards = string.Join('.', Enumerable.Repeat("*", 11));

        // when
        var act = () => TransportNaming.WildcardToRegex(manyWildcards);

        // then
        act.Should().Throw<ArgumentException>().WithMessage("MessageName pattern contains too many wildcards*");
    }

    [Theory]
    [InlineData("a*")]
    [InlineData("a#")]
    [InlineData("*")]
    [InlineData("#")]
    public void should_use_possessive_quantifiers_to_prevent_redos(string wildcard)
    {
        // when
        var pattern = TransportNaming.WildcardToRegex(wildcard);

        // then
        // Possessive quantifiers use atomic groups (?>...) which prevent backtracking entirely
        pattern.Should().Contain("(?>", "possessive quantifiers (atomic groups) eliminate ReDoS attacks");
    }

    [Fact]
    public void should_complete_regex_matching_within_timeout()
    {
        // given - worst-case input designed to exploit backtracking
        var pattern = TransportNaming.WildcardToRegex("user.*.created");
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
        var pattern = TransportNaming.WildcardToRegex("foo.*.bar");
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
        const string wildcardWithSpecialChars = "user.order[123].items";

        // when
        var pattern = TransportNaming.WildcardToRegex(wildcardWithSpecialChars);
        var regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));

        // then
        regex.IsMatch("user.order[123].items").Should().BeTrue();
        regex.IsMatch("user.order123.items").Should().BeFalse();
    }
}

#pragma warning disable MA0036 // MA0036: used as a reflection target for the IsController test; making it static (abstract+sealed in IL) would change the test's expectations.
public sealed class HomeController;
#pragma warning restore MA0036

public abstract class AbstractController;
