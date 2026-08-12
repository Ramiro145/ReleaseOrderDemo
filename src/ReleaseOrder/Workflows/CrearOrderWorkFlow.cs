using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Temporalio.Workflows;
using ReleaseOrderDemo.Activities;

namespace ReleaseOrderDemo.Workflows
{
    [Workflow]
    public class CrearOrdenWorkflow
    {
        private static readonly ActivityOptions DefaultOptions =
            new() { StartToCloseTimeout = TimeSpan.FromSeconds(30) };

        [WorkflowRun]
        public async Task<string> RunAsync(int orderId, int productId, int quantity)
        {
            // Stack de compensaciones — Temporal garantiza su durabilidad ante fallos del worker.
            // No se necesita un SagaBuilder personalizado: el event sourcing de Temporal persiste
            // cada ExecuteActivityAsync y reconstruye el estado del workflow en caso de replay.
            var compensations = new Stack<Func<Task>>();

            try
            {
                // Paso 1: marcar orden como "Created"
                await Workflow.ExecuteActivityAsync(
                    (OrderStatusActivities a) => a.UpdateOrderStatusAsync(orderId, "Created"),
                    DefaultOptions);
                compensations.Push(() => Workflow.ExecuteActivityAsync(
                    (OrderStatusActivities a) => a.UpdateOrderStatusAsync(orderId, "Failed"),
                    DefaultOptions));

                // Paso 2: reservar inventario
                await Workflow.ExecuteActivityAsync(
                    (InventoryActivities a) => a.ReserveInventoryAsync(orderId, productId, quantity),
                    DefaultOptions);
                compensations.Push(() => Workflow.ExecuteActivityAsync(
                    (InventoryActivities a) => a.CancelInventoryAsync(orderId, productId, quantity),
                    DefaultOptions));

                // Paso 3: marcar orden como "Completed"
                await Workflow.ExecuteActivityAsync(
                    (OrderStatusActivities a) => a.UpdateOrderStatusAsync(orderId, "Completed"),
                    DefaultOptions);

                return $"Orden {orderId} creada exitosamente";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Workflow] SAGA fallida para orden {orderId}: {ex.Message}. Iniciando compensación...");

                // Compensar en orden LIFO — Temporal persiste este bloque aunque el worker caiga
                while (compensations.TryPop(out var compensate))
                {
                    try
                    {
                        await compensate();
                    }
                    catch (Exception compEx)
                    {
                        Console.WriteLine($"[Workflow] Compensación fallida: {compEx.Message}");
                    }
                }

                return $"Orden {orderId} fallida y compensada";
            }
        }
    }
}
