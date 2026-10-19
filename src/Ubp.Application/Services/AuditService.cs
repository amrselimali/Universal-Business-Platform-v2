using Ubp.Core.Models;
using Ubp.Core.Security;
using Ubp.Infrastructure.Repositories;

namespace Ubp.Application.Services;

public sealed class AuditService
{
    private readonly IAuditRepository _repository;

    public AuditService(IAuditRepository repository) => _repository = repository;

    public Task WriteAsync(ActorContext actor, string tableName, string recordId, string action,
        object? oldData, object? newData, CancellationToken ct = default) =>
        _repository.WriteAsync(new AuditWrite
        {
            UserId = actor.UserId,
            BranchId = actor.CurrentBranchId,
            TableName = tableName,
            RecordId = recordId,
            Action = action,
            OldData = oldData,
            NewData = newData,
            IpAddress = actor.IpAddress,
            TraceId = actor.TraceId
        }, ct);

    public Task LoginAsync(Guid? userId, Guid? branchId, string action, string? ip, string traceId,
        CancellationToken ct = default) =>
        _repository.WriteAsync(new AuditWrite
        {
            UserId = userId, BranchId = branchId, TableName = "users",
            RecordId = userId?.ToString() ?? "anonymous", Action = action,
            IpAddress = ip, TraceId = traceId
        }, ct);
}
