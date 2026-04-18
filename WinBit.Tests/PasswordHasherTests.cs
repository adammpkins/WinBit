using FluentAssertions;
using WinBit.Core.WebUi;
using Xunit;

namespace WinBit.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_produces_two_base64_segments_joined_by_colon()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");
        hash.Split(':').Should().HaveCount(2);
    }

    [Fact]
    public void Hash_is_salted_so_two_runs_produce_different_output()
    {
        var a = PasswordHasher.Hash("pw");
        var b = PasswordHasher.Hash("pw");
        a.Should().NotBe(b);
    }

    [Fact]
    public void Verify_returns_true_only_for_the_original_password()
    {
        var hash = PasswordHasher.Hash("pw");

        PasswordHasher.Verify("pw", hash).Should().BeTrue();
        PasswordHasher.Verify("PW", hash).Should().BeFalse();
        PasswordHasher.Verify("other", hash).Should().BeFalse();
        PasswordHasher.Verify("", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("only-one-segment:")]
    [InlineData(":only-one-segment")]
    [InlineData("@@@:@@@")]   // invalid base64
    public void Verify_rejects_malformed_stored_hash(string? stored)
    {
        PasswordHasher.Verify("pw", stored!).Should().BeFalse();
    }
}
