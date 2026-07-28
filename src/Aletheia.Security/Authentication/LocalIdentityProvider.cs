using System.Security.Cryptography;
using System.Text;
using Aletheia.Foundation.Security;
using Aletheia.Security.Services;

namespace Aletheia.Security.Authentication;

public sealed class LocalIdentityProvider : IIdentityProvider
{
    private const string Pbkdf2Algorithm = "PBKDF2-SHA256";
    private const int Pbkdf2Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int KeyBytes = 32;

    private readonly IUserStore _userStore;

    public string Name => "Local";

    public LocalIdentityProvider(IUserStore userStore)
    {
        _userStore = userStore ?? throw new ArgumentNullException(nameof(userStore));
    }

    public async Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user == null || !user.IsEnabled)
        {
            return false;
        }

        var isValid = VerifyPassword(password, user.PasswordSalt, user.PasswordHash);
        if (isValid && !IsCurrentHash(user.PasswordHash))
        {
            user.PasswordSalt = GenerateSalt();
            user.PasswordHash = HashPassword(password, user.PasswordSalt);
            await _userStore.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        }

        return isValid;
    }

    public async Task<UserIdentity?> ResolveIdentityAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await _userStore.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (user == null || !user.IsEnabled)
        {
            return null;
        }

        var identity = new UserIdentity(
            user.UserId,
            user.Username,
            user.Email,
            user.DisplayName,
            user.Roles,
            Name);

        return identity;
    }

    public static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            password,
            saltBytes,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyBytes);

        return $"{Pbkdf2Algorithm}${Pbkdf2Iterations}${Convert.ToBase64String(key)}";
    }

    public static string GenerateSalt()
    {
        var bytes = new byte[SaltBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static bool VerifyPassword(string password, string salt, string storedHash)
    {
        if (IsCurrentHash(storedHash))
        {
            var computedHash = HashPassword(password, salt);
            return FixedTimeEquals(computedHash, storedHash);
        }

        var legacyHash = HashLegacyPassword(password, salt);
        return FixedTimeEquals(legacyHash, storedHash);
    }

    private static bool IsCurrentHash(string storedHash)
    {
        return storedHash.StartsWith($"{Pbkdf2Algorithm}$", StringComparison.Ordinal);
    }

    private static string HashLegacyPassword(string password, string salt)
    {
        var input = password + salt;
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
