using Framework.Api.Identity.TokenProviders;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Identity.TokenProviders;

public sealed class TotpRfc6238GeneratorTests
{
    [Fact]
    public void generate_and_validate_code_should_succeed()
    {
        //given
        var fixedTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "supersecretkey"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);
        const int variance = 2;
        const string modifier = "user@example.com";

        //when
        var code = generator.GenerateCode(secret, timestep, modifier);

        //then
        generator.ValidateCode(secret, code, timestep, variance, modifier).Should().BeTrue();
    }

    [Fact]
    public void validate_code_with_wrong_code_should_fail()
    {
        //given
        var fixedTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "supersecretkey"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);

        //when
        var result = generator.ValidateCode(secret, 123456, timestep, 2, null);

        //then
        result.Should().BeFalse();
    }

    [Fact]
    public void generate_code_with_null_secret_should_throw()
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);

        //when
        Action action = () => generator.GenerateCode(null!, TimeSpan.FromMinutes(3));

        //then
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void validate_code_with_null_secret_should_throw()
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);

        //when
        Action action = () => generator.ValidateCode(null!, 123456, TimeSpan.FromMinutes(3));

        //then
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void generate_and_validate_code_with_different_timestep_should_succeed(int minutes)
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "secret"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(minutes);

        //when
        var code = generator.GenerateCode(secret, timestep);

        //then
        generator.ValidateCode(secret, code, timestep, 2).Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void validate_code_with_different_variance_should_behave_correctly(int variance)
    {
        //given
        var fixedTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedTime);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "secret"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);
        var code = generator.GenerateCode(secret, timestep);

        //when
        var result = generator.ValidateCode(secret, code, timestep, variance);

        //then
        result.Should().BeTrue();
    }

    [Fact]
    public void generate_and_validate_code_with_null_modifier_should_succeed()
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "secret"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);

        //when
        var code = generator.GenerateCode(secret, timestep, null);

        //then
        generator.ValidateCode(secret, code, timestep, 2, null).Should().BeTrue();
    }

    [Fact]
    public void generate_and_validate_code_with_empty_modifier_should_succeed()
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "secret"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);

        //when
        var code = generator.GenerateCode(secret, timestep, "");

        //then
        generator.ValidateCode(secret, code, timestep, 2, "").Should().BeTrue();
    }

    [Fact]
    public void generate_and_validate_code_with_different_modifiers_should_fail()
    {
        //given
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var generator = new TotpRfc6238Generator(timeProvider);
        var secret = "secret"u8.ToArray();
        var timestep = TimeSpan.FromMinutes(3);
        var code = generator.GenerateCode(secret, timestep, "modifier1");

        //when
        var result = generator.ValidateCode(secret, code, timestep, 2, "modifier2");

        //then
        result.Should().BeFalse();
    }

    [Fact]
    public void validate_code_at_timestep_boundary_should_succeed_within_variance()
    {
        //given
        var baseTime = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var timestep = TimeSpan.FromMinutes(3);
        var secret = "secret"u8.ToArray();
        var timeProvider1 = new FakeTimeProvider(baseTime.Add(timestep).AddSeconds(-1));
        var generator = new TotpRfc6238Generator(timeProvider1);
        var code = generator.GenerateCode(secret, timestep);

        //when
        timeProvider1.Advance(timestep);

        //then
        generator.ValidateCode(secret, code, timestep, 1).Should().BeTrue();
    }
}
