using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Ubp.Application.Services;
using Ubp.Core.Security;

namespace Ubp.Web.Services;

public sealed class UserContext
{
    private readonly AuthenticationStateProvider _authState;
    private readonly AuthService _auth;
    private readonly UiCulture _culture;
    private readonly IHttpContextAccessor _http;
    private Task<ActorContext?>? _loadTask;

    public UserContext(AuthenticationStateProvider authState, AuthService auth, UiCulture culture,
        IHttpContextAccessor http)
    {
        _authState = authState; _auth = auth; _culture = culture; _http = http;
    }

    public Task<ActorContext?> GetAsync() => _loadTask ??= LoadAsync();

    public async Task<ActorContext> RequireAsync() =>
        await GetAsync() ?? throw new UnauthorizedAccessException("SESSION_EXPIRED");

    public void Reset() => _loadTask = null;

    private async Task<ActorContext?> LoadAsync()
    {
        var state = await _authState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated != true) return null;
        if (!Guid.TryParse(state.User.FindFirst("session_id")?.Value, out var sessionId)) return null;

        var traceId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var ip = _http.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var session = await _auth.ValidateSessionAsync(sessionId, ip, traceId);
        if (session is null) return null;

        _culture.Language = session.UserLanguage;
        return new ActorContext
        {
            UserId = session.User.Id,
            Username = session.User.Username,
            Name = session.User.Name,
            IsAdmin = session.User.IsAdmin,
            IsSuperAdmin = session.User.IsSuperAdmin,
            Language = session.UserLanguage,
            CurrentBranchId = session.BranchId,
            AllowedBranchIds = session.BranchIds,
            IpAddress = ip,
            TraceId = traceId
        };
    }
}
