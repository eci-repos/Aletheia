using System.Security.Cryptography;
using System.Text;
using Aletheia.Security.Authentication;
using Aletheia.Security.Services;

namespace Repository.UnitTests;

public sealed class SecurityHardeningTests
{
    [Fact]
    public async Task LocalIdentityProvider_validates_pbkdf2_hashes()
    {
        var store = new InMemoryUserStore();
        var salt = LocalIdentityProvider.GenerateSalt();
        await store.AddAsync(new UserRecord(
            "user-1",
            "admin",
            "admin@aletheia.local",
            "Administrator",
            LocalIdentityProvider.HashPassword("correct-password", salt),
            salt));

        var provider = new LocalIdentityProvider(store);

        Assert.True(await provider.ValidateCredentialsAsync("admin", "correct-password"));
        Assert.False(await provider.ValidateCredentialsAsync("admin", "wrong-password"));
    }

    [Fact]
    public async Task LocalIdentityProvider_accepts_and_upgrades_legacy_sha256_hashes()
    {
        var store = new InMemoryUserStore();
        var legacySalt = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        await store.AddAsync(new UserRecord(
            "user-1",
            "admin",
            "admin@aletheia.local",
            "Administrator",
            HashLegacyPassword("correct-password", legacySalt),
            legacySalt));

        var provider = new LocalIdentityProvider(store);

        Assert.True(await provider.ValidateCredentialsAsync("admin", "correct-password"));
        var upgraded = (await store.GetByUsernameAsync("admin"))!;
        Assert.StartsWith("PBKDF2-SHA256$", upgraded.PasswordHash);
        Assert.NotEqual(legacySalt, upgraded.PasswordSalt);
    }

    private static string HashLegacyPassword(string password, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password + salt);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
