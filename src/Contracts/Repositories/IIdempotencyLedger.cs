namespace Contracts.Repositories;

public record LedgerEntry(string IdempotencyKey, string? ResultJson);

public interface IIdempotencyLedger
{
    Task<LedgerEntry?> TryGetAsync(string key);

    // Devuelve false si la key ya existía (colisión concurrente); en ese caso el llamador re-lee con TryGetAsync.
    Task<bool> SaveAsync(string key, string workflowId, string activityType, int orderId, string? resultJson);
}
