using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Worker;

namespace Common
{
    public static class WorkerHost
    {
        /// <summary>
        /// Registra un workflow y múltiples clases de actividades resueltas desde el contenedor DI.
        /// Cada instancia de actividad se resuelve vía GetRequiredService para respetar el ciclo de vida configurado.
        /// </summary>
        public static async Task RunAsync<TWorkflow>(
            string taskQueue,
            IServiceProvider serviceProvider,
            IEnumerable<Type> activityTypes,
            IEnumerable<Type>? additionalWorkflowTypes = null,
            CancellationToken cancellationToken = default)
            where TWorkflow : class
        {
            var temporalTarget = Environment.GetEnvironmentVariable("TEMPORAL_HOST") ?? "temporal:7233";

            var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
            {
                TargetHost = temporalTarget
            });

            var options = new TemporalWorkerOptions(taskQueue)
                .AddWorkflow<TWorkflow>();

            // Child Workflows deben registrarse en el mismo Worker que los ejecuta
            // (aquí, la misma task queue del parent) para que puedan resolverse.
            if (additionalWorkflowTypes is not null)
                foreach (var workflowType in additionalWorkflowTypes)
                    options.AddWorkflow(workflowType);

            foreach (var activityType in activityTypes)
            {
                var instance = serviceProvider.GetRequiredService(activityType);
                options.AddAllActivities(activityType, instance);
            }

            using var worker = new TemporalWorker(client, options);

            Console.WriteLine($"Worker listening on '{taskQueue}'...");
            await worker.ExecuteAsync(cancellationToken);
        }

        /// <summary>
        /// Overload original para compatibilidad con workers que tienen una sola clase de actividades.
        /// </summary>
        public static async Task RunAsync<TWorkflow, TActivities>(
            string taskQueue,
            TActivities activities,
            CancellationToken cancellationToken = default)
            where TWorkflow : class
            where TActivities : class
        {
            var temporalTarget = Environment.GetEnvironmentVariable("TEMPORAL_HOST") ?? "temporal:7233";

            var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
            {
                TargetHost = temporalTarget
            });

            var options = new TemporalWorkerOptions(taskQueue)
                .AddAllActivities(activities)
                .AddWorkflow<TWorkflow>();

            using var worker = new TemporalWorker(client, options);

            Console.WriteLine($"Worker listening on '{taskQueue}'...");
            await worker.ExecuteAsync(cancellationToken);
        }
    }
}
