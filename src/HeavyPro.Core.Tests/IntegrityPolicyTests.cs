using HeavyPro.Core.Physics;
using Xunit;

namespace HeavyPro.Core.Tests;

public class IntegrityPolicyTests
{
    [Fact]
    public void Unsigned_build_is_allowed_so_an_unsigned_release_still_runs()
    {
        // No certificate in use yet: the check must be a no-op, or every honest customer is locked out.
        Assert.Equal(IntegrityVerdict.Allow, IntegrityPolicy.Decide(signingActive: false, allFilesTrusted: false, publisherMatches: false, out _));
        Assert.Equal(IntegrityVerdict.Allow, IntegrityPolicy.Decide(signingActive: false, allFilesTrusted: true, publisherMatches: true, out _));
    }

    [Fact]
    public void Signed_and_intact_is_allowed()
    {
        Assert.Equal(IntegrityVerdict.Allow, IntegrityPolicy.Decide(signingActive: true, allFilesTrusted: true, publisherMatches: true, out var reason));
        Assert.Equal("", reason);
    }

    [Theory]
    [InlineData(false, true)]   // a file's signature no longer verifies (patched, or unsigned)
    [InlineData(true, false)]   // signed, but by a pirate's own certificate, not Rotaidem
    [InlineData(false, false)]
    public void Signed_build_refuses_a_modified_or_foreign_signed_file(bool allFilesTrusted, bool publisherMatches)
    {
        Assert.Equal(IntegrityVerdict.Tampered,
            IntegrityPolicy.Decide(signingActive: true, allFilesTrusted, publisherMatches, out var reason));
        Assert.Contains("rotaidem.com", reason);
    }
}
