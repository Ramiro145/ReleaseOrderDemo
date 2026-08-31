USE OrdersDb;
GO

-- Ledger de idempotencia para las actividades de escritura del worker ReleaseOrder.
-- Cada fila registra una actividad ya ejecutada (key = "{WorkflowId}:{ActivityType}:{OrderId}")
-- junto con su resultado serializado, para que un reintento at-least-once de Temporal
-- devuelva el resultado guardado en vez de volver a aplicar el efecto.
-- Script aditivo y re-aplicable: no hace nada si la tabla ya existe.
IF OBJECT_ID('dbo.ProcessedActivities', 'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProcessedActivities] (
        [IdempotencyKey] NVARCHAR(300) NOT NULL,   -- "{WorkflowId}:{ActivityType}:{OrderId}"
        [WorkflowId]     NVARCHAR(200) NOT NULL,
        [ActivityType]   NVARCHAR(100) NOT NULL,
        [OrderId]        INT           NOT NULL,
        [ResultJson]     NVARCHAR(MAX) NULL,        -- resultado serializado; NULL para actividades void
        [CreatedAt]      DATETIME      NOT NULL CONSTRAINT [DF_ProcessedActivities_CreatedAt] DEFAULT (getdate()),
        CONSTRAINT [PK_ProcessedActivities] PRIMARY KEY CLUSTERED ([IdempotencyKey] ASC)
    );
END;
GO
