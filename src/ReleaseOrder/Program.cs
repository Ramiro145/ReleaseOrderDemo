using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ReleaseOrderDemo.Activities;
using ReleaseOrderDemo.Workflows;
using ReleaseOrderDemo.Infrastructure;
using Common;

namespace ReleaseOrderDemo
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var workerName = args.Length > 0 ? args[0].ToLower() : "release";
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION")
                ?? "Server=db;Database=OrdersDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

            var taskQueue = $"{workerName}-order-task-queue";
            var mode = args.Length > 1 ? args[1].ToLower() : "worker";

            switch (mode)
            {
                case "worker":
                    // Construir el contenedor DI con todos los servicios y actividades
                    var services = new ServiceCollection();
                    services.AddReleaseOrderServices(connectionString);
                    var provider = services.BuildServiceProvider();

                    var activityTypes = new[]
                    {
                        typeof(InventoryActivities),
                        typeof(PaymentActivities),
                        typeof(ShippingActivities),
                        typeof(OrderStatusActivities),
                        typeof(OrderLookupActivities)
                    };

                    if (workerName == "release")
                        await WorkerHost.RunAsync<ReleaseOrderWorkflow>(
                            taskQueue,
                            provider,
                            activityTypes,
                            additionalWorkflowTypes: new[] { typeof(ShippingWorkflow) });
                    else
                        await WorkerHost.RunAsync<CrearOrdenWorkflow>(taskQueue, provider, activityTypes);

                    break;

                case "release":
                    await WorkflowStarter.StartAsync<ReleaseOrderWorkflow, string>(
                        taskQueue,
                        wf => wf.RunAsync(
                            args.Length > 2 ? Convert.ToInt32(args[2]) : 1),
                        "release-order"
                    );
                    break;

                case "create":
                    await WorkflowStarter.StartAsync<CrearOrdenWorkflow, string>(
                        taskQueue,
                        wf => wf.RunAsync(
                            args.Length > 2 ? Convert.ToInt32(args[2]) : 1,
                            args.Length > 3 ? Convert.ToInt32(args[3]) : 1,
                            args.Length > 4 ? Convert.ToInt32(args[4]) : 1),
                        "create-order"
                    );
                    break;
            }
        }
    }
}
