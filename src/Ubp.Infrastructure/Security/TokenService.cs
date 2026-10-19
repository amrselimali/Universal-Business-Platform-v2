using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Ubp.Core.Models;

namespace Ubp.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "UniversalBusinessPlatform";
    public string Audience { get; set; } = "UniversalBusinessPlatform";
    public string Key { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
}

public sealed class TokenService
{
    private readonly JwtOptions _options;

    public TokenService(JwtOptions options) => _options = options;

    public string CreateAccessToken(User user, Guid sessionId, Guid? branchId)
    {
        if (string.IsNullOrWhiteSpace(_options.Key) || _options.Key.Length < 32)
            throw new InvalidOperationException("JWT_KEY_NOT_CONFIGURED");

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new("name", user.Name ?? user.Username),
            new("is_admin", user.IsAdmin ? "true" : "false"),
            new("is_super_admin", user.IsSuperAdmin ? "true" : "false"),
            new("session_id", sessionId.ToString()),
            new("language", user.Language)
        };
        if (branchId.HasValue)
            claims.Add(new Claim("branch_id", branchId.Value.ToString()));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
}
