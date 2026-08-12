using Microsoft.Extensions.DependencyInjection;
using Temporalio.Client;
using Temporalio.Worker;
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
    .AddAllActivities(activities)
    .AddWorkflow<OrderReportWorkflow>();

using var worker = new TemporalWorker(client, options);

Console.WriteLine("OrderReport worker listening on 'report-task-queue'...");
await worker.ExecuteAsync(CancellationToken.None);