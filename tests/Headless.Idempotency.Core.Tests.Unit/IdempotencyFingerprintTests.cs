// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using Headless.Idempotency;
using Headless.Testing.Tests;

namespace Tests;

public sealed class IdempotencyFingerprintTests : TestBase
{
    [Fact]
    public void should_compute_v1_as_sha256_of_the_canonical_payload_stably_across_runs()
    {
        // when
        var fingerprint = IdempotencyFingerprint.Compute("hello");

        // then — a fixed digest, so a change to the hashing breaks this test instead of every stored record
        fingerprint.Algorithm.Should().Be(IdempotencyFingerprint.V1);
        Convert
            .ToHexStringLower(fingerprint.Hash.Span)
            .Should()
            .Be("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
        fingerprint.ToString().Should().StartWith("v1:2cf24dba");
    }

    [Fact]
    public void should_hash_text_as_its_utf8_bytes()
    {
        var fromText = IdempotencyFingerprint.Compute("طلب-1");
        var fromBytes = IdempotencyFingerprint.Compute(Encoding.UTF8.GetBytes("طلب-1"));

        fromText.Should().Be(fromBytes);
        fromText.GetHashCode().Should().Be(fromBytes.GetHashCode());
    }

    [Fact]
    public void should_match_the_same_request_and_not_a_different_one()
    {
        var fingerprint = IdempotencyFingerprint.Compute("a");

        fingerprint.Matches(IdempotencyFingerprint.Compute("a")).Should().BeTrue();
        fingerprint.Matches(IdempotencyFingerprint.Compute("b")).Should().BeFalse();
    }

    [Fact]
    public void should_refuse_to_compare_against_a_stored_unknown_algorithm()
    {
        // given
        var fingerprint = IdempotencyFingerprint.Compute("a");
        var stored = new IdempotencyFingerprint("v2", fingerprint.Hash.Span);

        // when
        var act = () => fingerprint.Matches(stored);

        // then
        act.Should().Throw<NotSupportedException>().WithMessage("*'v2'*cannot be recomputed*");
    }

    [Fact]
    public void should_copy_the_digest_so_the_caller_cannot_change_it()
    {
        // given
        byte[] digest = [1, 2, 3];
        var fingerprint = new IdempotencyFingerprint("v1", digest);

        // when
        digest[0] = 9;

        // then
        fingerprint.Hash.ToArray().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void should_refuse_an_empty_digest()
    {
        var act = () => new IdempotencyFingerprint("v1", []);

        act.Should().Throw<ArgumentException>();
    }
}
