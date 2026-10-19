using BCrypt.Net;

namespace Ubp.Infrastructure.Security;

public sealed class PasswordHasher
{
    private const int WorkFactor = 12;
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("dummy-password-for-timing", WorkFactor);

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (SaltParseException)
        {
            return false;
        }
    }

    /// <summary>يُستخدم لمساواة زمن الاستجابة عندما لا يكون المستخدم موجوداً.</summary>
    public void FakeVerify(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash);
}
