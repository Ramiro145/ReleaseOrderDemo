using System;
using System.Text.Json;
using System.Threading.Tasks;
using Temporalio.Activities;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Activities
{
    /// <summary>
    /// Envuelve la ejecución de una actividad de escritura para tolerar el at-least-once
    /// de Temporal: consulta el ledger de idempotencia por una clave estable
    /// ("{WorkflowId}:{ActivityType}:{OrderId}"); si hay hit devuelve el resultado
    /// guardado sin volver a llamar al servicio; si no, ejecuta el delegado y persiste
    /// (key, resultado). Ante colisión concurrente (SaveAsync == false) re-lee el ledger.
    /// </summary>
    public static class IdempotentActivity
    {
        /// <summary>Sobrecarga para actividades void.</summary>
        public static async Task RunAsync(IIdempotencyLedger ledger, int orderId, Func<Task> action)
        {
            var info = ActivityExecutionContext.Current.Info;
            var key = BuildKey(info.WorkflowId, info.ActivityType, orderId);

            var existing = await ledger.TryGetAsync(key);
            if (existing is not null)
            {
                Console.WriteLine($"[Ledger] hit {key} (void), skipping activity body");
                return;
            }

            await action();

            var saved = await ledger.SaveAsync(key, info.WorkflowId, info.ActivityType, orderId, null);
            if (!saved)
                Console.WriteLine($"[Ledger] hit {key} (void) after concurrent collision");
        }

        /// <summary>Sobrecarga para actividades con resultado.</summary>
        public static async Task<T> RunAsync<T>(IIdempotencyLedger ledger, int orderId, Func<Task<T>> action)
        {
            var info = ActivityExecutionContext.Current.Info;
            var key = BuildKey(info.WorkflowId, info.ActivityType, orderId);

            var existing = await ledger.TryGetAsync(key);
            if (existing is not null)
            {
                Console.WriteLine($"[Ledger] hit {key}, returning stored result");
                return Deserialize<T>(existing.ResultJson);
            }

            var result = await action();
            var resultJson = JsonSerializer.Serialize(result);

            var saved = await ledger.SaveAsync(key, info.WorkflowId, info.ActivityType, orderId, resultJson);
            if (!saved)
            {
                // Otro intento concurrente ganó la carrera: re-leemos su resultado.
                var winner = await ledger.TryGetAsync(key);
                Console.WriteLine($"[Ledger] hit {key} after concurrent collision, returning stored result");
                return winner is not null ? Deserialize<T>(winner.ResultJson) : result;
            }

            return result;
        }

        private static string BuildKey(string workflowId, string activityType, int orderId)
            => $"{workflowId}:{activityType}:{orderId}";

        private static T Deserialize<T>(string? json)
            => json is null ? default! : JsonSerializer.Deserialize<T>(json)!;
    }
}
