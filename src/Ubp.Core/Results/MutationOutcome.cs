namespace Ubp.Core.Results;

public sealed record MutationOutcome(bool Deleted, bool Deactivated, string MessageKey);
