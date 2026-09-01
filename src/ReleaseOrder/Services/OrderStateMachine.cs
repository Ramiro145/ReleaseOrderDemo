using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Contracts.Repositories;

namespace ReleaseOrderDemo.Services
{
    /// <summary>
    /// Implementación de <see cref="IOrderStateMachine"/> sobre dbo.Orders/Products/Shipments.
    /// Mismo patrón que los repos existentes: connection string por constructor, una
    /// SqlConnection nueva por llamada. Cada método ejecuta un único batch T-SQL: lee
    /// Status con UPDLOCK/ROWLOCK, decide si el paso ya se aplicó, y si no, aplica el
    /// efecto de dominio y avanza Status en la misma transacción. El UPDLOCK serializa
    /// dos intentos concurrentes sobre la misma orden sin locks explícitos en C#.
    /// </summary>
    public class OrderStateMachine : IOrderStateMachine
    {
        private readonly string _connectionString;

        public OrderStateMachine(string connectionString)
        {
            _connectionString = connectionString;
        }

        public Task<StepOutcome> TryReserveInventoryAsync(int orderId, int productId, int quantity)
        {
            const string sql = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @current NVARCHAR(50);
                SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

                IF @current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 3;
                END
                ELSE IF @current IN ('InventoryReserved', 'PaymentProcessed', 'Completed', 'Shipped')
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Products SET Stock = Stock - @Quantity
                      WHERE ProductId = @ProductId AND IsActive = 1 AND Stock >= @Quantity;

                    IF @@ROWCOUNT = 0
                    BEGIN
                        ROLLBACK TRANSACTION;
                        SELECT 2;
                    END
                    ELSE
                    BEGIN
                        UPDATE dbo.Orders SET Status = 'InventoryReserved', UpdatedAt = GETDATE()
                          WHERE OrderId = @OrderId;
                        COMMIT TRANSACTION;
                        SELECT 0;
                    END
                END
                """;

            return ExecuteStepAsync(sql, "InventoryReserved", orderId, cmd =>
            {
                cmd.Parameters.AddWithValue("@OrderId", orderId);
                cmd.Parameters.AddWithValue("@ProductId", productId);
                cmd.Parameters.AddWithValue("@Quantity", quantity);
            });
        }

        public Task<StepOutcome> TryCancelInventoryAsync(int orderId, int productId, int quantity)
        {
            const string sql = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @current NVARCHAR(50);
                SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

                IF @current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 3;
                END
                ELSE IF @current IN ('InventoryCanceled', 'Compensated', 'CompensationFailed')
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Products SET Stock = Stock + @Quantity WHERE ProductId = @ProductId;
                    UPDATE dbo.Orders SET Status = 'InventoryCanceled', UpdatedAt = GETDATE()
                      WHERE OrderId = @OrderId;
                    COMMIT TRANSACTION;
                    SELECT 0;
                END
                """;

            return ExecuteStepAsync(sql, "InventoryCanceled", orderId, cmd =>
            {
                cmd.Parameters.AddWithValue("@OrderId", orderId);
                cmd.Parameters.AddWithValue("@ProductId", productId);
                cmd.Parameters.AddWithValue("@Quantity", quantity);
            });
        }

        public Task<StepOutcome> TryMarkPaymentProcessedAsync(int orderId)
        {
            const string sql = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @current NVARCHAR(50);
                SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

                IF @current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 3;
                END
                ELSE IF @current IN ('PaymentProcessed', 'Completed', 'Shipped')
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Orders SET Status = 'PaymentProcessed', UpdatedAt = GETDATE()
                      WHERE OrderId = @OrderId;
                    COMMIT TRANSACTION;
                    SELECT 0;
                END
                """;

            return ExecuteStepAsync(sql, "PaymentProcessed", orderId,
                cmd => cmd.Parameters.AddWithValue("@OrderId", orderId));
        }

        public Task<StepOutcome> TryMarkPaymentRefundedAsync(int orderId)
        {
            const string sql = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @current NVARCHAR(50);
                SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

                IF @current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 3;
                END
                ELSE IF @current IN ('PaymentRefunded', 'InventoryCanceled', 'Compensated')
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    UPDATE dbo.Orders SET Status = 'PaymentRefunded', UpdatedAt = GETDATE()
                      WHERE OrderId = @OrderId;
                    COMMIT TRANSACTION;
                    SELECT 0;
                END
                """;

            return ExecuteStepAsync(sql, "PaymentRefunded", orderId,
                cmd => cmd.Parameters.AddWithValue("@OrderId", orderId));
        }

        public Task<StepOutcome> TryShipAsync(int orderId, string address)
        {
            const string sql = """
                SET XACT_ABORT ON;
                BEGIN TRANSACTION;

                DECLARE @current NVARCHAR(50);
                SELECT @current = Status FROM dbo.Orders WITH (UPDLOCK, ROWLOCK) WHERE OrderId = @OrderId;

                IF @current IS NULL
                BEGIN
                    ROLLBACK TRANSACTION;
                    SELECT 3;
                END
                ELSE IF @current = 'Shipped'
                BEGIN
                    COMMIT TRANSACTION;
                    SELECT 1;
                END
                ELSE
                BEGIN
                    INSERT INTO dbo.Shipments (OrderId, Address, Status, CreatedAt)
                      VALUES (@OrderId, @Address, 'Shipped', GETDATE());
                    UPDATE dbo.Orders SET Status = 'Shipped', UpdatedAt = GETDATE()
                      WHERE OrderId = @OrderId;
                    COMMIT TRANSACTION;
                    SELECT 0;
                END
                """;

            return ExecuteStepAsync(sql, "Shipped", orderId, cmd =>
            {
                cmd.Parameters.AddWithValue("@OrderId", orderId);
                cmd.Parameters.AddWithValue("@Address", address);
            });
        }

        private async Task<StepOutcome> ExecuteStepAsync(
            string sql, string targetStatus, int orderId, Action<SqlCommand> bindParameters)
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var cmd = new SqlCommand(sql, conn);
            bindParameters(cmd);

            var code = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            var outcome = (StepOutcome)code;

            Console.WriteLine(outcome switch
            {
                StepOutcome.AlreadyApplied =>
                    $"[State] order {orderId} already '{targetStatus}'; skipping (idempotent retry)",
                StepOutcome.Applied =>
                    $"[State] order {orderId} advanced to '{targetStatus}'",
                _ => $"[State] order {orderId} step to '{targetStatus}' outcome: {outcome}"
            });

            return outcome;
        }
    }
}
