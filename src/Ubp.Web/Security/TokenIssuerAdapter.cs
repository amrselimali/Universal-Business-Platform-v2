using Ubp.Application.Contracts;
using Ubp.Core.Models;
using Ubp.Infrastructure.Security;

namespace Ubp.Web.Security;

public sealed class TokenIssuerAdapter : IAccessTokenIssuer
{
    private readonly TokenService _tokens;

    public TokenIssuerAdapter(TokenService tokens) => _tokens = tokens;

    public string Create(User user, Guid sessionId, Guid? branchId) =>
        _tokens.CreateAccessToken(user, sessionId, branchId);
}
