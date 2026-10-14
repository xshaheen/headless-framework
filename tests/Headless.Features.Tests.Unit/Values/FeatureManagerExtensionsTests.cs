// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless;
using Headless.Features;
using Headless.Primitives;
using Headless.Testing.Tests;
using NSubstitute.ExceptionExtensions;

namespace Tests.Values;

public sealed class FeatureManagerExtensionsTests : TestBase
{
    private readonly IFeatureManager _featureManager = Substitute.For<IFeatureManager>();

    private void _SetupValues(params (string Name, string? Value)[] values)
    {
        _featureManager
            .GetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), AbortToken)
            .Returns(call =>
            {
                var name = call.ArgAt<string>(0);
                var value = values.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal)).Value;

                return new FeatureValue(name, value, Provider: null);
            });
        _featureManager
            .GetAllAsync(Arg.Any<IReadOnlySet<string>>(), AbortToken)
            .Returns(call =>
            {
                var names = call.ArgAt<IReadOnlySet<string>>(0);

                return names.ToDictionary(
                    name => name,
                    name => new FeatureValue(
                        name,
                        values.FirstOrDefault(v => string.Equals(v.Name, name, StringComparison.Ordinal)).Value,
                        Provider: null
                    ),
                    StringComparer.Ordinal
                );
            });
    }

    #region IsEnabledAsync (multi-name)

    [Fact]
    public async Task should_resolve_a_multi_name_check_through_one_batched_read()
    {
        // given
        _SetupValues(("A", "true"), ("B", "false"));

        // when
        var result = await _featureManager.IsEnabledAsync(requiresAll: false, ["A", "B"], AbortToken);

        // then — one GetAllAsync carries both names; no per-name GetAsync ran
        result.Should().BeTrue();
        await _featureManager.Received(1).GetAllAsync(Arg.Any<IReadOnlySet<string>>(), AbortToken);
        await _featureManager
            .DidNotReceive()
            .GetAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_require_every_name_when_requires_all()
    {
        // given
        _SetupValues(("A", "true"), ("B", "false"));

        // when
        var all = await _featureManager.IsEnabledAsync(requiresAll: true, ["A", "B"], AbortToken);
        var any = await _featureManager.IsEnabledAsync(requiresAll: false, ["A", "B"], AbortToken);

        // then
        all.Should().BeFalse();
        any.Should().BeTrue();
    }

    [Fact]
    public async Task should_treat_a_value_less_feature_as_disabled()
    {
        // given — A has no value anywhere in the chain, so the batch carries it with a null value
        _SetupValues(("A", null));

        // when
        var all = await _featureManager.IsEnabledAsync(requiresAll: true, ["A"], AbortToken);
        var any = await _featureManager.IsEnabledAsync(requiresAll: false, ["A"], AbortToken);

        // then
        all.Should().BeFalse();
        any.Should().BeFalse();
    }

    [Fact]
    public async Task should_report_a_not_defined_feature_in_a_multi_name_check()
    {
        // given — the batch omits the name, which is what GetAllAsync does for an undefined feature, and the
        // manager's single-name read reports it as not defined
        _featureManager
            .GetAllAsync(Arg.Any<IReadOnlySet<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, FeatureValue>(StringComparer.Ordinal));
        _featureManager
            .GetAsync("Missing", Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new ConflictException(new ErrorDescriptor("features:undefined", "The feature 'Missing' is undefined."))
            );

        // when
        var act = async () => await _featureManager.IsEnabledAsync(requiresAll: true, ["Missing"], AbortToken);

        // then — GetAsync reproduces the manager's not-defined error, as the per-name loop did
        await act.Should().ThrowAsync<ConflictException>();
        await _featureManager.Received(1).GetAsync("Missing", null, null, true, AbortToken);
    }

    [Fact]
    public async Task should_throw_when_a_stored_value_is_not_a_boolean()
    {
        // given
        _SetupValues(("A", "yes-please"));

        // when
        var act = async () => await _featureManager.IsEnabledAsync(requiresAll: true, ["A"], AbortToken);

        // then
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion IsEnabledAsync (multi-name)

    #region EnsureEnabledAsync (multi-name)

    [Fact]
    public async Task should_resolve_a_multi_name_ensure_through_one_batched_read()
    {
        // given
        _SetupValues(("A", "true"), ("B", "true"));

        // when
        var act = () => _featureManager.EnsureEnabledAsync(requiresAll: true, ["A", "B"], AbortToken);

        // then
        await act.Should().NotThrowAsync();
        await _featureManager.Received(1).GetAllAsync(Arg.Any<IReadOnlySet<string>>(), AbortToken);
        await _featureManager
            .DidNotReceive()
            .GetAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_throw_the_and_error_when_any_required_feature_is_disabled()
    {
        // given
        _SetupValues(("A", "true"), ("B", "false"));

        // when
        var act = () => _featureManager.EnsureEnabledAsync(requiresAll: true, ["A", "B"], AbortToken);

        // then
        var error = (await act.Should().ThrowAsync<ConflictException>()).Which.Errors.Should().ContainSingle().Which;
        error.Code.Should().Be("g:feature_currently_not_available");
        error.Params!.Should().ContainKey("Type").WhoseValue.Should().Be("And");
        error.Params!.Should().ContainKey("FeatureNames").WhoseValue.Should().BeEquivalentTo(new[] { "A", "B" });
    }

    [Fact]
    public async Task should_throw_the_or_error_when_no_feature_is_enabled()
    {
        // given
        _SetupValues(("A", "false"), ("B", "false"));

        // when
        var act = () => _featureManager.EnsureEnabledAsync(requiresAll: false, ["A", "B"], AbortToken);

        // then
        var error = (await act.Should().ThrowAsync<ConflictException>()).Which.Errors.Should().ContainSingle().Which;
        error.Params!.Should().ContainKey("Type").WhoseValue.Should().Be("Or");
    }

    [Fact]
    public async Task should_return_without_throwing_when_any_feature_is_enabled()
    {
        // given
        _SetupValues(("A", "false"), ("B", "true"));

        // when
        var act = () => _featureManager.EnsureEnabledAsync(requiresAll: false, ["A", "B"], AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }

    #endregion EnsureEnabledAsync (multi-name)
}
