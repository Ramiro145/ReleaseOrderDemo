USE OrdersDb;
GO

-- Corrige datos de demostración que apunten a productos inexistentes.
-- La demo inicial contiene el producto 1.
UPDATE o
SET ProductId = 1
FROM dbo.Orders o
LEFT JOIN dbo.Products p ON p.ProductId = o.ProductId
WHERE p.ProductId IS NULL;
GO

-- Elimina cualquier FK actual entre Orders y Products sin asumir su nombre.
DECLARE @constraintName sysname;
DECLARE @sql nvarchar(max);

SELECT TOP (1) @constraintName = fk.name
FROM sys.foreign_keys fk
WHERE fk.parent_object_id = OBJECT_ID('dbo.Orders')
  AND fk.referenced_object_id = OBJECT_ID('dbo.Products');

IF @constraintName IS NOT NULL
BEGIN
    SET @sql = N'ALTER TABLE dbo.Orders DROP CONSTRAINT '
        + QUOTENAME(@constraintName) + N';';
    EXEC sys.sp_executesql @sql;
END;
GO

ALTER TABLE dbo.Orders
WITH CHECK ADD CONSTRAINT FK_Orders_Products
FOREIGN KEY (ProductId)
REFERENCES dbo.Products(ProductId);
GO
