using Temporalio.Client;
using Contracts.Workflows;
using Microsoft.Data.SqlClient;
using Contracts.Dtos;
using Contracts;
using Common;
using Microsoft.VisualBasic;
using Temporalio.Exceptions;
using Grpc.Core;


var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("OrdersDb");
builder.Services.AddTransient(_ => new SqlConnection(connectionString));

// Configurar cliente Temporal
builder.Services.AddSingleton(sp =>
{
    var client = TemporalClient.ConnectAsync(new TemporalClientConnectOptions
    {
        TargetHost = "temporal:7233" // nombre del servicio temporal en docker-compose
    }).Result;
    return client;
});

// Agregar servicios de Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Order API",
        Version = "v1",
        Description = "API para gestionar órdenes y reportes con Temporal",
        Contact = new Microsoft.OpenApi.OpenApiContact
        {
            Name = "Equipo Demo",
            Email = "demo@example.com"
        }
    });
});

builder.WebHost.UseUrls("http://0.0.0.0:5000;http://0.0.0.0:5001");

var app = builder.Build();

// Habilitar Swagger SIEMPRE (no solo en Development)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Order API v1");
    c.RoutePrefix = "swagger"; // UI en http://localhost:5000/swagger
});

// Middleware opcional
// app.UseHttpsRedirection();

// Endpoint: iniciar workflow de orden
app.MapPost("/orders/{orderId}/release", async (
    int orderId,
    TemporalClient client) =>
{
    var handle = await client.StartWorkflowAsync<IReleaseOrderWorkflow>(
        wf => wf.RunAsync(orderId),
        new WorkflowOptions
        {
            Id = $"release-order-{orderId}",
            TaskQueue = "release-order-task-queue"
        });
    return Results.Ok(new
    {
        WorkflowId = handle.Id,
        NextStep = $"POST /orders/{orderId}/release/decision"
    });
});

// Endpoint: enviar una decisión externa al Workflow mediante Signal.
app.MapPost("/orders/{orderId}/release/decision", async (
    int orderId,
    ReleaseDecision decision,
    TemporalClient client) =>
{
    var workflowId = $"release-order-{orderId}";
    var handle = client.GetWorkflowHandle<IReleaseOrderWorkflow>(workflowId);

    await handle.SignalAsync(
        wf => wf.SubmitReleaseDecisionAsync(decision));

    return Results.Accepted($"/orders/{orderId}/status", new
    {
        WorkflowId = workflowId,
        decision.Approved,
        decision.Reason
    });
});

// Endpoint: consultar estado de orden
app.MapGet("/orders/{orderId}/status", async (int orderId, TemporalClient client) =>
{
    // var handle = client.GetWorkflowHandle<IReleaseOrderWorkflow>($"release-order-{orderId}");
    var status = string.Empty;
    // return Results.Ok(new { OrderId = orderId, Status = status });
    var workflowId = $"release-order-{orderId}";
    var result = await WorkflowValidator.ValidateWorkflowAsync(client, workflowId);

    if (!result.Exists)
    {
        return Results.NotFound(new
        {
            WorkflowId = workflowId,
            Error = result.Error
        });
    }
    try
    {
        // Si existe, ahora sí puedes consultar GetStatus()
        var handle = client.GetWorkflowHandle(workflowId);
        status = await handle.QueryAsync<string>("GetStatus", []);
    }
    catch (Temporalio.Exceptions.RpcException ex)
        when (ex.InnerException is Grpc.Core.RpcException grpcEx
              && grpcEx.StatusCode == StatusCode.DeadlineExceeded)
    {
        return Results.Ok(new
        {
            status = result.Info?.Status.ToString()
        });
    }
    catch (Temporalio.Exceptions.RpcException ex)
    {
        var grpc = ex.InnerException as Grpc.Core.RpcException;

        return Results.Problem(new
        {
            Error = ex.Message,
            GrpcStatus = grpc?.StatusCode.ToString(),
            Details = grpc?.Status.Detail
        }.ToString());
    }

    return Results.Ok(new
    {
        WorkflowId = workflowId,
        Status = status,
        State = result.Info?.Status.ToString()
    });
});

// Endpoint: generar reporte
app.MapGet("/reports/{orderId}",
    async (int orderId, TemporalClient client) =>
{
    var handle = await client.StartWorkflowAsync<IOrderReportWorkflow>(
        wf => wf.RunAsync(orderId),
        new WorkflowOptions
        {
            Id = $"report-order-{orderId}",
            TaskQueue = "report-task-queue"
        });

    var result = await handle.GetResultAsync<OrderReportResult>();
    return Results.Ok(new { OrderId = orderId, Report = result });
});


// Endpoint: listar órdenes
app.MapGet("/orders", async (SqlConnection conn) =>
{
    await conn.OpenAsync();
    var cmd = new SqlCommand("SELECT OrderId, OrderCode, ProductId, Status, CreatedAt, Address, Amount, Quantity, UpdatedAt FROM Orders", conn);
    var reader = await cmd.ExecuteReaderAsync();

    var orders = new List<OrderDto>();
    while (await reader.ReadAsync())
    {
        orders.Add(new OrderDto
        {
            OrderId = reader.GetInt32(0),
            OrderCode = reader.GetString(1),
            ProductId = reader.GetInt32(2),
            Status = reader.GetString(3),
            CreatedAt = reader.GetDateTime(4),
            Address = reader.GetString(5),
            Amount = reader.GetDecimal(6),
            Quantity = reader.GetInt32(7),
            UpdatedAt = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8)
        });
    }

    return Results.Ok(orders);
});

app.MapPost("/orders", async (CreateOrderRequest request, SqlConnection conn) =>
{
    await conn.OpenAsync();

    var cmd = new SqlCommand(@"
        INSERT INTO Orders (ProductId, OrderCode, Quantity, Amount, Status, Address, CreatedAt)
        OUTPUT INSERTED.OrderId
        VALUES (@ProductId, @OrderCode, @Quantity, @Amount, @Status, @Address, @CreatedAt)", conn);

    cmd.Parameters.AddWithValue("@ProductId", request.ProductId);
    cmd.Parameters.AddWithValue("@OrderCode", request.OrderCode);
    cmd.Parameters.AddWithValue("@Quantity", request.Quantity);
    cmd.Parameters.AddWithValue("@Amount", request.Amount);
    cmd.Parameters.AddWithValue("@Status", "Created");
    cmd.Parameters.AddWithValue("@Address", request.Address);
    cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);

    var result = await cmd.ExecuteScalarAsync();
    var newOrderId = Convert.ToInt32(result);

    return Results.Created($"/orders/{newOrderId}", new
    {
        OrderId = newOrderId,
        request.ProductId,
        request.Amount,
        Status = "Created"
    });
});


app.Run();
