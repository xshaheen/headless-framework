// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Headless;
using Headless.Testing.Tests;

namespace Tests.Core;

/// <summary>
/// The failure fingerprint groups failures by cause: the innermost exception type, after unwrapping
/// <see cref="AggregateException"/> and <see cref="TargetInvocationException"/>, plus the top three stack frames outside
/// <c>System.*</c> and <c>Microsoft.*</c>, each as a method full name with compiler-generated names reduced to their
/// declaring method, hashed with SHA-256 and truncated to 16 hex characters behind a <c>v1:</c> prefix.
/// </summary>
public sealed class FailureFingerprintTests : TestBase
{
    private const string _Self = "Tests.Core.FailureFingerprintTests";

    [Fact]
    public void should_keep_the_v1_output_when_the_exception_was_never_thrown()
    {
        // SHA-256 of "System.InvalidOperationException", computed outside this code. A change here breaks every
        // fingerprint already stored, so it must come with a new version prefix instead.
        FailureFingerprint.VersionPrefix.Should().Be("v1:");
        FailureFingerprint.Compute(new InvalidOperationException("x")).Should().Be("v1:386c08bd726234e4");
    }

    [Fact]
    public void should_produce_one_fingerprint_when_the_same_failure_carries_different_ids_in_its_message()
    {
        var first = _Catch(() => _ThrowForOrder("order 4f1c-11"));
        var second = _Catch(() => _ThrowForOrder("order 9a2b-77"));

        first.Message.Should().NotBe(second.Message);
        var fingerprint = FailureFingerprint.Compute(first);

        fingerprint.Should().Be(FailureFingerprint.Compute(second));
        fingerprint
            .Should()
            .Be(
                _Expected(
                    "System.InvalidOperationException",
                    $"{_Self}._ThrowForOrder",
                    // The lambda passed to _Catch reduces to the test method that declares it.
                    $"{_Self}.should_produce_one_fingerprint_when_the_same_failure_carries_different_ids_in_its_message",
                    $"{_Self}._Catch"
                )
            );
    }

    [Fact]
    public void should_fingerprint_the_innermost_exception_when_it_is_wrapped_in_aggregate_and_target_invocation()
    {
        var timeout = _Catch(_ThrowTimeout);

        var expected = _Expected("System.TimeoutException", $"{_Self}._ThrowTimeout", $"{_Self}._Catch");

        FailureFingerprint.Compute(timeout).Should().Be(expected);
        FailureFingerprint.Compute(new AggregateException(timeout)).Should().Be(expected);
        FailureFingerprint.Compute(new TargetInvocationException(timeout)).Should().Be(expected);
        FailureFingerprint
            .Compute(new AggregateException(new TargetInvocationException(timeout)))
            .Should()
            .Be(expected);
    }

    [Fact]
    public void should_fingerprint_the_wrapper_when_the_code_chose_to_wrap_the_exception()
    {
        var wrapped = new InvalidOperationException("save failed", _Catch(_ThrowTimeout));

        FailureFingerprint.Compute(wrapped).Should().Be(_Expected("System.InvalidOperationException"));
    }

    [Fact]
    public void should_skip_system_frames_and_keep_only_the_top_three_application_frames()
    {
        // int.Parse throws from System.Number, two System frames below the first application frame.
        var exception = _Catch(() => _Outer("not a number"));

        exception.StackTrace.Should().Contain("System.Number");
        FailureFingerprint
            .Compute(exception)
            .Should()
            .Be(_Expected("System.FormatException", $"{_Self}._Parse", $"{_Self}._Middle", $"{_Self}._Outer"));
    }

    [Fact]
    public async Task should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method()
    {
        Func<Task> lambda = async () => await _FailAsync("run 1");

        Exception? caught = null;
        try
        {
            await lambda();
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught!.StackTrace.Should().Contain("MoveNext");
        FailureFingerprint
            .Compute(caught)
            .Should()
            .Be(
                _Expected(
                    "System.InvalidOperationException",
                    $"{_Self}._FailAsync",
                    $"{_Self}.should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method",
                    $"{_Self}.should_reduce_async_state_machine_and_lambda_frames_to_their_declaring_method"
                )
            );
    }

    [Fact]
    public void should_throw_when_the_exception_is_null()
    {
        var act = () => FailureFingerprint.Compute(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("lease-lapsed")]
    [InlineData("node-not-alive")]
    [InlineData("retry-budget-exhausted-by-recovery")]
    [InlineData("http-503")]
    // Fifteen and seventeen hex digits cannot equal a 16-character computed hash.
    [InlineData("abcdef012345678")]
    [InlineData("abcdef01234567890")]
    // Sixteen characters that are not all hex digits cannot either.
    [InlineData("abcdef012345678g")]
    public void should_prefix_the_cause_with_the_version_when_the_cause_is_valid(string cause)
    {
        FailureFingerprint.ForCause(cause).Should().Be("v1:" + cause);
    }

    [Theory]
    [InlineData("Lease-Lapsed")]
    [InlineData("lease lapsed")]
    [InlineData("lease_lapsed")]
    [InlineData("lease:lapsed")]
    [InlineData("v1:lease-lapsed")]
    [InlineData("délai")]
    public void should_refuse_a_cause_when_it_has_a_character_outside_lowercase_letters_digits_and_hyphens(string cause)
    {
        var act = () => FailureFingerprint.ForCause(cause);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("cause");
    }

    [Theory]
    [InlineData("386c08bd726234e4")]
    [InlineData("0000000000000000")]
    [InlineData("abcdefabcdefabcd")]
    public void should_refuse_a_cause_when_it_could_equal_a_computed_fingerprint(string cause)
    {
        var act = () => FailureFingerprint.ForCause(cause);

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("cause");
    }

    [Fact]
    public void should_refuse_a_cause_when_it_is_empty()
    {
        var act = () => FailureFingerprint.ForCause("");

        act.Should().Throw<ArgumentException>();
    }

    #region Helpers

    private static string _Expected(string exceptionType, params string[] frames)
    {
        var input = string.Join('\n', [exceptionType, .. frames]);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

        return "v1:" + hash[..16];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception _Catch(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
#pragma warning disable ERP022 // False positive: the caught exception is the helper's return value, which every test asserts on.
            return ex;
#pragma warning restore ERP022
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _ThrowForOrder(string message) => throw new InvalidOperationException(message);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _ThrowTimeout() => throw new TimeoutException("timed out after 30 s");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Outer(string value) => _Middle(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Middle(string value) => _Parse(value);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Parse(string value) => _ = int.Parse(value, CultureInfo.InvariantCulture);

    private static async Task _FailAsync(string id)
    {
        await Task.Yield();

        throw new InvalidOperationException($"failed {id}");
    }

    #endregion
}
