-- Crear base de datos
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'OrdersDb')
BEGIN
    CREATE DATABASE OrdersDb;
END
GO

USE OrdersDb;
GO

-- Tabla de productos
IF OBJECT_ID('Products', 'U') IS NULL
BEGIN
    CREATE TABLE Products (
        Id INT PRIMARY KEY IDENTITY(1,1),
        Code NVARCHAR(50) NOT NULL,
        Name NVARCHAR(100) NOT NULL,
        Stock INT NOT NULL
    );
END
GO

-- Tabla de órdenes
IF OBJECT_ID('Orders', 'U') IS NULL
BEGIN
    CREATE TABLE Orders (
        Id INT PRIMARY KEY IDENTITY(1,1),
        OrderCode NVARCHAR(50) NOT NULL,
        ProductCode NVARCHAR(50) NOT NULL,
        Quantity INT NOT NULL,
        Status NVARCHAR(50) NOT NULL,
        CreatedAt DATETIME NOT NULL DEFAULT GETDATE()
    );
END
GO

-- Tabla de envíos
IF OBJECT_ID('Shipments', 'U') IS NULL
BEGIN
    CREATE TABLE Shipments (
        Id INT PRIMARY KEY IDENTITY(1,1),
        OrderCode NVARCHAR(50) NOT NULL,
        Address NVARCHAR(200) NOT NULL,
        ShippedAt DATETIME NULL
    );
END
GO

-- Datos dummy
INSERT INTO Products (Code, Name, Stock) VALUES
('P-1001', 'Laptop', 10),
('P-1002', 'Mouse', 50),
('P-1003', 'Keyboard', 30);

INSERT INTO Orders (OrderCode, ProductCode, Quantity, Status) VALUES
('ORD-1001', 'P-1001', 1, 'Pending'),
('ORD-1002', 'P-1002', 2, 'Pending');

INSERT INTO Shipments (OrderCode, Address) VALUES
('ORD-1001', 'Av. Siempre Viva 123, Monterrey'),
('ORD-1002', 'Calle Falsa 456, Escobedo');
GO