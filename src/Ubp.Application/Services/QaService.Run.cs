using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using Ubp.Core.Models;
using Ubp.Core.Results;
using Ubp.Core.Security;
using Ubp.Infrastructure.Data;

namespace Ubp.Application.Services;

public sealed partial class QaService
{
    public async Task<QaRunSummary> RunAsync(ActorContext actor, IReadOnlyList<string> selectedCodes,
        CancellationToken ct = default)
    {
        await _permissions.RequireAsync(actor, ScreenCodes.QaCenter, ActionCodes.RunTests, ct: ct);
        EnsureIsolatedEnvironment();

        var codes = selectedCodes.Count == 0
            ? Registry.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : selectedCodes.Where(c => Registry.Any(r => r.Code.Equals(c, StringComparison.OrdinalIgnoreCase)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0) throw new ValidationAppException("qa.no_tests_selected");

        var revision = typeof(QaService).Assembly.GetName().Version?.ToString() ?? "dev";
        var runId = await _repository.CreateRunAsync(actor.UserId, "QA_TEST", revision, actor.TraceId, ct);
        var results = new List<QaTestResult>();

        foreach (var definition in Registry.Where(r => codes.Contains(r.Code)))
        {
            var timer = Stopwatch.StartNew();
            QaTestResult result;
            try
            {
                var outcome = await ExecuteAsync(definition.Code, ct);
                result = new QaTestResult
                {
                    TestCode = definition.Code,
                    Status = outcome.Passed ? "Passed" : "Failed",
                    Message = outcome.Message,
                    TraceId = actor.TraceId
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QA test {Code} errored", definition.Code);
                result = new QaTestResult
                {
                    TestCode = definition.Code, Status = "Error",
                    Message = "Infrastructure error", TraceId = actor.TraceId
                };
            }
            timer.Stop();
            result.DurationMs = (int)timer.ElapsedMilliseconds;
            results.Add(result);
            await _repository.AddResultAsync(runId, result, ct);
        }

        var passed = results.Count(r => r.Status == "Passed");
        var failed = results.Count - passed;
        var status = failed == 0 ? "Completed" : "Failed";
        await _repository.CompleteRunAsync(runId, status, passed, failed, ct);
        return new QaRunSummary
        {
            Id = runId, Environment = "QA_TEST", CodeRevision = revision, Status = status,
            TotalCount = results.Count, PassedCount = passed, FailedCount = failed,
            TraceId = actor.TraceId, StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow
        };
    }

    private void EnsureIsolatedEnvironment()
    {
        if (string.IsNullOrWhiteSpace(_options.QaConnectionString))
            throw new ValidationAppException("qa.database_not_configured");
        var app = new NpgsqlConnectionStringBuilder(_options.ConnectionString);
        var qa = new NpgsqlConnectionStringBuilder(_options.QaConnectionString);
        var sameServer = string.Equals(app.Host, qa.Host, StringComparison.OrdinalIgnoreCase)
                         && app.Port == qa.Port
                         && string.Equals(app.Database, qa.Database, StringComparison.OrdinalIgnoreCase);
        if (sameServer) throw new ValidationAppException("qa.isolation_required");
    }

    private async Task<(bool Passed, string Message)> ExecuteAsync(string code,
        CancellationToken ct) => code switch
    {
        "PERMISSIONS_DEFAULT_DENY" => Pure(!PermissionEvaluator.Evaluate(
            Array.Empty<PermissionEntry>(), Array.Empty<PermissionEntry>(), false,
            ScreenCodes.Branches, ActionCodes.View).Allowed),
        "PERMISSIONS_ROLE_GRANT" => Pure(PermissionEvaluator.Evaluate(
            new[] { new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.Branches } },
            Array.Empty<PermissionEntry>(), false, ScreenCodes.Branches, ActionCodes.View).Allowed),
        "PERMISSIONS_USER_DENY" => Pure(!PermissionEvaluator.Evaluate(
            new[] { new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = true, Scope = PermissionScope.All } },
            new[] { new PermissionEntry { ScreenCode = ScreenCodes.Branches, ActionCode = ActionCodes.View,
                IsAllowed = false } }, false, ScreenCodes.Branches, ActionCodes.View).Allowed),
        "PERMISSIONS_ADMIN_BYPASS" => Pure(PermissionEvaluator.Evaluate(
            Array.Empty<PermissionEntry>(), Array.Empty<PermissionEntry>(), true,
            ScreenCodes.DatabaseAdmin, ActionCodes.RunMigrations).Allowed),
        "AR_NORMALIZATION" => await DbTestAsync(async (connection, tx) =>
        {
            var ok = await ScalarAsync<bool>(connection, tx,
                "SELECT normalize_search('أحمد مُحَمَّد') = normalize_search('احمد محمد')");
            return ok;
        }, ct),
        "SETTINGS_SINGLETON" => await DbTestAsync(async (connection, tx) =>
            await ScalarAsync<int>(connection, tx, "SELECT count(*)::int FROM app_settings") <= 1, ct),
        "BASE_CURRENCY_SINGLE" => await DbTestAsync(async (connection, tx) =>
            await ScalarAsync<int>(connection, tx,
                "SELECT count(*)::int FROM currencies WHERE is_base") <= 1, ct),
        "AUDIT_IMMUTABLE" => await AuditImmutabilityAsync(ct),
        "VERSION_CONCURRENCY" => await VersionConcurrencyAsync(ct),
        _ => (false, "Unknown test")
    };

    private static (bool, string) Pure(bool passed) =>
        (passed, passed ? "OK" : "Assertion failed");
}
