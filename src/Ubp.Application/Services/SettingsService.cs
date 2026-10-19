using Microsoft.Extensions.Caching.Memory;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public interface ISettingsService
{
    Task<AppSettings> GetAsync(CancellationToken ct = default);
    Task<AppSettings> UpdateAsync(ActorContext actor, AppSettings input, CancellationToken ct = default);
    Task<string?> ValidatePasswordAsync(string password, string username, CancellationToken ct = default);
    void Invalidate();
}

public sealed class SettingsService : ISettingsService
{
    private const string CacheKey = "app:settings";
    private readonly ISettingsRepository _repository;
    private readonly IAuditRepository _audit;
    private readonly PermissionService _permissions;
    private readonly IMemoryCache _cache;

    public SettingsService(ISettingsRepository repository, IAuditRepository audit,
        PermissionService permissions, IMemoryCache cache)
    {
        _repository = repository;
        _audit = audit;
        _permissions = permissions;
        _cache = cache;
    }

    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue(CacheKey, out AppSettings? cached) && cached is not null)
            return cached;

        var settings = await _repository.GetAsync(ct)
            ?? new AppSettings { Id = Guid.Empty, CompanyName = "Universal Business Platform" };
        _cache.Set(CacheKey, settings, TimeSpan.FromMinutes(2));
        return settings;
    }

    public async Task<AppSettings> UpdateAsync(ActorContext actor, AppSettings input,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.SettingsGeneral, ActionCodes.Edit, ct: ct);
        var old = await _repository.GetAsync(ct) ?? throw new NotFoundAppException();
        input.Id = old.Id;
        input.Version = old.Version;

        if (input.SessionTimeoutMinutes is < 5 or > 480)
            throw new ValidationAppException("validation.session_timeout");
        if (input.PasswordMinLength is < 8 or > 64)
            throw new ValidationAppException("validation.password_min_length");
        if (input.MaxFailedAttempts is < 1 or > 20)
            throw new ValidationAppException("validation.max_attempts");
        if (input.LockoutMinutes is < 1 or > 1440)
            throw new ValidationAppException("validation.lockout_minutes");
        if (string.IsNullOrWhiteSpace(input.CompanyName))
            throw new ValidationAppException("validation.company_name_required");

        var updated = await _repository.UpdateAsync(input, ct)
            ?? throw new ConflictException("error.version_conflict");

        Invalidate();
        await _audit.WriteAsync(new AuditWrite
        {
            UserId = actor.UserId, BranchId = actor.CurrentBranchId,
            TableName = "app_settings", RecordId = updated.Id.ToString(),
            Action = "UPDATE", OldData = old, NewData = updated,
            IpAddress = actor.IpAddress, TraceId = actor.TraceId
        }, ct);
        return updated;
    }

    public async Task<string?> ValidatePasswordAsync(string password, string username,
        CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);
        if (password.Length < settings.PasswordMinLength) return "validation.password_short";
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            return "validation.password_same_as_username";
        return null;
    }

    public void Invalidate() => _cache.Remove(CacheKey);
}
