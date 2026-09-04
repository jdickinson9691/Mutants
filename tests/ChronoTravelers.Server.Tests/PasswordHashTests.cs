namespace ChronoTravelers.Server.Tests;

/// <summary>
/// PBKDF2-SHA256 create/verify round trip (PasswordHash.cs). `internal` —
/// reachable here only via the InternalsVisibleTo in AssemblyInfo.cs.
/// </summary>
public class PasswordHashTests
{
    [Fact]
    public void Create_ThenVerify_WithTheRightPassword_Succeeds()
    {
        var (salt, hash) = PasswordHash.Create("correct horse battery staple");

        Assert.True(PasswordHash.Verify("correct horse battery staple", salt, hash));
    }

    [Fact]
    public void Verify_WithTheWrongPassword_Fails()
    {
        var (salt, hash) = PasswordHash.Create("correct horse battery staple");

        Assert.False(PasswordHash.Verify("wrong password", salt, hash));
    }

    [Fact]
    public void Verify_IsCaseSensitive()
    {
        var (salt, hash) = PasswordHash.Create("Sensitive1");

        Assert.False(PasswordHash.Verify("sensitive1", salt, hash));
    }

    [Fact]
    public void Create_NeverReusesASaltAcrossCalls()
    {
        var a = PasswordHash.Create("same password");
        var b = PasswordHash.Create("same password");

        Assert.NotEqual(a.Salt, b.Salt);
        // Different salts should (overwhelmingly likely) produce different hashes too.
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Verify_WithMalformedBase64Salt_ReturnsFalseRatherThanThrowing()
    {
        var (_, hash) = PasswordHash.Create("whatever");

        var ok = PasswordHash.Verify("whatever", "not-valid-base64!!", hash);

        Assert.False(ok);
    }

    [Fact]
    public void Verify_WithMalformedBase64Hash_ReturnsFalseRatherThanThrowing()
    {
        var (salt, _) = PasswordHash.Create("whatever");

        var ok = PasswordHash.Verify("whatever", salt, "not-valid-base64!!");

        Assert.False(ok);
    }

    [Fact]
    public void Verify_AgainstAnEmptyPassword_DoesNotThrow()
    {
        var (salt, hash) = PasswordHash.Create("");

        Assert.True(PasswordHash.Verify("", salt, hash));
        Assert.False(PasswordHash.Verify("not empty", salt, hash));
    }
}
