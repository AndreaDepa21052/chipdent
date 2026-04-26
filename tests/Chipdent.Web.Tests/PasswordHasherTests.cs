using Chipdent.Web.Infrastructure.Identity;

namespace Chipdent.Web.Tests;

public class PasswordHasherTests
{
    private readonly BCryptPasswordHasher _sut = new();

    [Fact]
    public void Hash_produces_non_empty_string()
    {
        var hash = _sut.Hash("supersecret");
        Assert.False(string.IsNullOrWhiteSpace(hash));
        Assert.NotEqual("supersecret", hash);
    }

    [Fact]
    public void Verify_returns_true_for_correct_password()
    {
        var hash = _sut.Hash("chipdent");
        Assert.True(_sut.Verify("chipdent", hash));
    }

    [Fact]
    public void Verify_returns_false_for_wrong_password()
    {
        var hash = _sut.Hash("chipdent");
        Assert.False(_sut.Verify("wrong", hash));
    }

    [Fact]
    public void Verify_returns_false_for_garbage_hash()
    {
        Assert.False(_sut.Verify("any", "not-a-bcrypt-hash"));
    }
}
