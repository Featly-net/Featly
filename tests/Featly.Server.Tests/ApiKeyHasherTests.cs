using Featly.Server.Authentication;
using FluentAssertions;
using Xunit;

namespace Featly.Server.Tests;

/// <summary>
/// <see cref="ApiKeyHasher"/> mints plaintext tokens, derives the indexed
/// prefix, hashes the full token with Argon2id for storage, and verifies an
/// incoming token against the stored hash in constant time. These tests
/// cover the happy path + the negative cases the auth pipeline relies on.
/// </summary>
public class ApiKeyHasherTests
{
    [Fact]
    public void GenerateToken_returns_distinct_tokens_with_expected_prefix()
    {
        var hasher = new ApiKeyHasher();

        var t1 = hasher.GenerateToken();
        var t2 = hasher.GenerateToken();

        t1.Should().StartWith("featly_");
        t2.Should().StartWith("featly_");
        t1.Should().NotBe(t2);
        t1.Length.Should().BeGreaterThan(20);
    }

    [Fact]
    public void ExtractPrefix_returns_12_characters_for_full_token()
    {
        var hasher = new ApiKeyHasher();
        var token = hasher.GenerateToken();

        var prefix = ApiKeyHasher.ExtractPrefix(token);

        prefix.Should().HaveLength(12);
        token.Should().StartWith(prefix);
    }

    [Fact]
    public void Hash_then_Verify_round_trips_the_correct_token()
    {
        var hasher = new ApiKeyHasher();
        var token = hasher.GenerateToken();
        var hash = hasher.Hash(token);

        hash.Should().StartWith("argon2id$");
        ApiKeyHasher.Verify(token, hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_a_wrong_token()
    {
        var hasher = new ApiKeyHasher();
        var token = hasher.GenerateToken();
        var hash = hasher.Hash(token);

        var wrong = hasher.GenerateToken();
        ApiKeyHasher.Verify(wrong, hash).Should().BeFalse();
    }

    [Fact]
    public void Verify_rejects_a_malformed_hash()
    {
        var hasher = new ApiKeyHasher();
        var token = hasher.GenerateToken();

        ApiKeyHasher.Verify(token, "not-a-hash").Should().BeFalse();
        ApiKeyHasher.Verify(token, "argon2id$v=19$bogus$abc$def").Should().BeFalse();
    }

    [Fact]
    public void Hash_produces_different_output_for_same_input_due_to_salt()
    {
        var hasher = new ApiKeyHasher();
        var token = hasher.GenerateToken();

        var h1 = hasher.Hash(token);
        var h2 = hasher.Hash(token);

        h1.Should().NotBe(h2, "salt is randomised per call");
        ApiKeyHasher.Verify(token, h1).Should().BeTrue();
        ApiKeyHasher.Verify(token, h2).Should().BeTrue();
    }
}
