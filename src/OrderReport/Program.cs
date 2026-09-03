using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Worker;
using Common;
using OrderReportDemo.Activities;
using OrderReportDemo.Workflows;
using OrderReportDemo.Infrastructure;

var temporalTarget = Environment.GetEnvironmentVariable("TEMPORAL_HOST") ?? "temporal:7233";
var connectionString = Environment.GetEnvironmentVariable("SQLSERVER_CONN")
    ?? "Server=sqlserver;Database=OrdersDb;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

// TemporalClient se construye antes del contenedor porque es async
// y se registra como singleton pre-construido
var client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
{
    TargetHost = temporalTarget
});

// Construir el contenedor DI
var services = new ServiceCollection();
services.AddSingleton(client);
services.AddOrderReportServices(connectionString);
var provider = services.BuildServiceProvider();

// Resolver la actividad desde el contenedor (DI inyecta IReportService y TemporalClient)
var activities = provider.GetRequiredService<OrderReportActivities>();

var options = new TemporalWorkerOptions("report-task-queue")
{
    // Mismo drenaje que los workers de ReleaseOrder (spec 04): ante SIGTERM /
    // Ctrl+C, esperar a las Activities en vuelo antes de cerrar.
    GracefulShutdownTimeout = WorkerHost.GracefulShutdownTimeout
};
options.AddAllActivities(activities);
options.AddWorkflow<OrderReportWorkflow>();

using var worker = new TemporalWorker(client, options);

await WorkerHost.RunWithGracefulShutdownAsync(worker, "report-task-queue");