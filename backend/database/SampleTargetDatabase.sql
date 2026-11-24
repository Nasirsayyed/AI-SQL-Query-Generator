/*
    AIQueryDemoDB — sample target (business) database for the AI SQL Query Generator.

    Run with sqlcmd so the read-only login password is supplied at run time and never committed:
        sqlcmd -S localhost -U sa -C -i SampleTargetDatabase.sql -v ReaderPassword="<strong password>"
    (In SSMS enable Query > SQLCMD Mode and add  :setvar ReaderPassword "<strong password>"  at the top.)

    Seed data is generated relative to GETDATE() so questions such as "orders created today", "monthly revenue for the
    current year" and "customers with no orders in the last 90 days" always return meaningful results.
    The script is re-runnable: it drops and recreates the demo tables.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

IF DB_ID(N'AIQueryDemoDB') IS NULL
    CREATE DATABASE AIQueryDemoDB;
GO

USE AIQueryDemoDB;
GO

DROP VIEW IF EXISTS dbo.vwMonthlySales;
DROP TABLE IF EXISTS dbo.Payments;
DROP TABLE IF EXISTS dbo.OrderDetails;
DROP TABLE IF EXISTS dbo.Orders;
DROP TABLE IF EXISTS dbo.Inventory;
DROP TABLE IF EXISTS dbo.Warehouses;
DROP TABLE IF EXISTS dbo.Products;
DROP TABLE IF EXISTS dbo.Customers;
GO

/* ------------------------------------------------------------------ Tables */

CREATE TABLE dbo.Customers
(
    CustomerId    int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    CustomerName  nvarchar(150)      NOT NULL,
    Email         nvarchar(256)      NOT NULL CONSTRAINT UQ_Customers_Email UNIQUE,
    City          nvarchar(100)      NOT NULL,
    Region        nvarchar(50)       NOT NULL,
    CreatedDate   datetime2(0)       NOT NULL CONSTRAINT DF_Customers_CreatedDate DEFAULT (SYSDATETIME())
);

CREATE TABLE dbo.Products
(
    ProductId     int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    ProductName   nvarchar(150)      NOT NULL CONSTRAINT UQ_Products_ProductName UNIQUE,
    Category      nvarchar(50)       NOT NULL,
    UnitPrice     decimal(10, 2)     NOT NULL CONSTRAINT CK_Products_UnitPrice CHECK (UnitPrice >= 0),
    ReorderLevel  int                NOT NULL CONSTRAINT CK_Products_ReorderLevel CHECK (ReorderLevel >= 0)
);

CREATE TABLE dbo.Warehouses
(
    WarehouseId   int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
    WarehouseName nvarchar(100)      NOT NULL CONSTRAINT UQ_Warehouses_WarehouseName UNIQUE,
    City          nvarchar(100)      NOT NULL
);

CREATE TABLE dbo.Inventory
(
    InventoryId       int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Inventory PRIMARY KEY,
    ProductId         int                NOT NULL CONSTRAINT FK_Inventory_Products REFERENCES dbo.Products (ProductId),
    WarehouseId       int                NOT NULL CONSTRAINT FK_Inventory_Warehouses REFERENCES dbo.Warehouses (WarehouseId),
    AvailableQuantity int                NOT NULL CONSTRAINT CK_Inventory_Available CHECK (AvailableQuantity >= 0),
    ReservedQuantity  int                NOT NULL CONSTRAINT CK_Inventory_Reserved CHECK (ReservedQuantity >= 0),
    LastUpdated       datetime2(0)       NOT NULL,
    CONSTRAINT UQ_Inventory_Product_Warehouse UNIQUE (ProductId, WarehouseId)
);

CREATE TABLE dbo.Orders
(
    OrderId       int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    CustomerId    int                NOT NULL CONSTRAINT FK_Orders_Customers REFERENCES dbo.Customers (CustomerId),
    OrderDate     datetime2(0)       NOT NULL,
    OrderStatus   nvarchar(20)       NOT NULL CONSTRAINT CK_Orders_Status CHECK (OrderStatus IN (N'Pending', N'Processing', N'Shipped', N'Delivered', N'Cancelled')),
    TotalAmount   decimal(12, 2)     NOT NULL CONSTRAINT DF_Orders_TotalAmount DEFAULT (0),
    WarehouseId   int                NOT NULL CONSTRAINT FK_Orders_Warehouses REFERENCES dbo.Warehouses (WarehouseId),
    ShippedDate   datetime2(0)       NULL
);

CREATE TABLE dbo.OrderDetails
(
    OrderDetailId int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_OrderDetails PRIMARY KEY,
    OrderId       int                NOT NULL CONSTRAINT FK_OrderDetails_Orders REFERENCES dbo.Orders (OrderId),
    ProductId     int                NOT NULL CONSTRAINT FK_OrderDetails_Products REFERENCES dbo.Products (ProductId),
    Quantity      int                NOT NULL CONSTRAINT CK_OrderDetails_Quantity CHECK (Quantity > 0),
    UnitPrice     decimal(10, 2)     NOT NULL,
    LineTotal     AS (CONVERT(decimal(12, 2), Quantity * UnitPrice)) PERSISTED
);

CREATE TABLE dbo.Payments
(
    PaymentId     int IDENTITY(1, 1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    OrderId       int                NOT NULL CONSTRAINT FK_Payments_Orders REFERENCES dbo.Orders (OrderId),
    PaymentDate   datetime2(0)       NOT NULL,
    PaymentAmount decimal(12, 2)     NOT NULL,
    PaymentStatus nvarchar(20)       NOT NULL CONSTRAINT CK_Payments_Status CHECK (PaymentStatus IN (N'Completed', N'Pending', N'Failed', N'Refunded'))
);
GO

/* ----------------------------------------------------------------- Indexes */

CREATE INDEX IX_Customers_Region ON dbo.Customers (Region) INCLUDE (CustomerName, City);
CREATE INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId) INCLUDE (OrderDate, TotalAmount);
CREATE INDEX IX_Orders_OrderDate ON dbo.Orders (OrderDate) INCLUDE (OrderStatus, TotalAmount, CustomerId);
CREATE INDEX IX_Orders_Warehouse_Status ON dbo.Orders (WarehouseId, OrderStatus);
CREATE INDEX IX_OrderDetails_OrderId ON dbo.OrderDetails (OrderId) INCLUDE (ProductId, Quantity, LineTotal);
CREATE INDEX IX_OrderDetails_ProductId ON dbo.OrderDetails (ProductId) INCLUDE (Quantity, LineTotal);
CREATE INDEX IX_Inventory_WarehouseId ON dbo.Inventory (WarehouseId);
CREATE INDEX IX_Payments_OrderId ON dbo.Payments (OrderId) INCLUDE (PaymentAmount, PaymentStatus);
GO

/* -------------------------------------------------------------------- View */

CREATE VIEW dbo.vwMonthlySales
AS
SELECT YEAR(o.OrderDate)  AS OrderYear,
       MONTH(o.OrderDate) AS OrderMonth,
       COUNT_BIG(*)       AS OrderCount,
       SUM(o.TotalAmount) AS Revenue
FROM dbo.Orders AS o
WHERE o.OrderStatus <> N'Cancelled'
GROUP BY YEAR(o.OrderDate), MONTH(o.OrderDate);
GO

/* ------------------------------------------------------ Descriptions (MS_Description) */

EXEC sys.sp_addextendedproperty N'MS_Description', N'Customers who place orders. Email is personal data.', N'SCHEMA', N'dbo', N'TABLE', N'Customers';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Sales region of the customer (North, South, East, West, Central).', N'SCHEMA', N'dbo', N'TABLE', N'Customers', N'COLUMN', N'Region';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Catalogue of products for sale.', N'SCHEMA', N'dbo', N'TABLE', N'Products';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Stock level below which the product should be reordered.', N'SCHEMA', N'dbo', N'TABLE', N'Products', N'COLUMN', N'ReorderLevel';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Fulfilment warehouses.', N'SCHEMA', N'dbo', N'TABLE', N'Warehouses';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Stock per product and warehouse.', N'SCHEMA', N'dbo', N'TABLE', N'Inventory';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Customer orders. TotalAmount is the sum of the order lines.', N'SCHEMA', N'dbo', N'TABLE', N'Orders';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Pending, Processing, Shipped, Delivered or Cancelled.', N'SCHEMA', N'dbo', N'TABLE', N'Orders', N'COLUMN', N'OrderStatus';
EXEC sys.sp_addextendedproperty N'MS_Description', N'When the order left the warehouse; NULL until shipped. Processing time = ShippedDate - OrderDate.', N'SCHEMA', N'dbo', N'TABLE', N'Orders', N'COLUMN', N'ShippedDate';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Order lines.', N'SCHEMA', N'dbo', N'TABLE', N'OrderDetails';
EXEC sys.sp_addextendedproperty N'MS_Description', N'Payments received for orders.', N'SCHEMA', N'dbo', N'TABLE', N'Payments';
GO

/* --------------------------------------------------------------- Seed data */

INSERT dbo.Warehouses (WarehouseName, City)
VALUES (N'Seattle Fulfilment Center', N'Seattle'),
       (N'Dallas Distribution Hub', N'Dallas'),
       (N'Chicago Central Depot', N'Chicago'),
       (N'Atlanta South Warehouse', N'Atlanta'),
       (N'Newark East Warehouse', N'Newark');

INSERT dbo.Products (ProductName, Category, UnitPrice, ReorderLevel)
VALUES (N'Wireless Mouse', N'Electronics', 24.99, 40),
       (N'Mechanical Keyboard', N'Electronics', 89.00, 25),
       (N'27-inch Monitor', N'Electronics', 279.00, 15),
       (N'USB-C Hub', N'Electronics', 39.50, 35),
       (N'Noise Cancelling Headphones', N'Electronics', 199.00, 20),
       (N'Webcam HD', N'Electronics', 59.90, 30),
       (N'Laptop Stand', N'Electronics', 34.00, 30),
       (N'Portable SSD 1TB', N'Electronics', 119.00, 20),
       (N'Ergonomic Office Chair', N'Furniture', 329.00, 10),
       (N'Standing Desk', N'Furniture', 549.00, 8),
       (N'Bookshelf', N'Furniture', 149.00, 10),
       (N'Filing Cabinet', N'Furniture', 189.00, 8),
       (N'Desk Lamp', N'Furniture', 45.00, 25),
       (N'Conference Table', N'Furniture', 899.00, 5),
       (N'A4 Copy Paper (Box)', N'Office Supplies', 29.99, 60),
       (N'Ballpoint Pens (50)', N'Office Supplies', 12.50, 80),
       (N'Sticky Notes Pack', N'Office Supplies', 8.75, 90),
       (N'Stapler', N'Office Supplies', 14.20, 40),
       (N'Whiteboard Markers', N'Office Supplies', 11.40, 60),
       (N'Notebook A5', N'Office Supplies', 6.90, 100),
       (N'Printer Toner Black', N'Office Supplies', 74.00, 30),
       (N'Coffee Beans 1kg', N'Breakroom', 22.00, 40),
       (N'Tea Assortment', N'Breakroom', 9.50, 50),
       (N'Paper Cups (100)', N'Breakroom', 7.80, 70),
       (N'Water Filter', N'Breakroom', 31.00, 20),
       (N'Microwave Oven', N'Breakroom', 129.00, 6),
       (N'First Aid Kit', N'Safety', 42.00, 25),
       (N'Safety Glasses', N'Safety', 9.90, 60),
       (N'Fire Extinguisher', N'Safety', 64.00, 12),
       (N'High-Visibility Vest', N'Safety', 12.00, 50),
       (N'Hand Sanitizer 500ml', N'Cleaning', 6.40, 80),
       (N'Disinfectant Wipes', N'Cleaning', 5.90, 90),
       (N'Trash Bags (50)', N'Cleaning', 13.30, 60),
       (N'Microfiber Cloths', N'Cleaning', 10.20, 50),
       (N'Floor Cleaner 5L', N'Cleaning', 27.50, 20),
       (N'Network Switch 24-port', N'Networking', 239.00, 8),
       (N'Wi-Fi Access Point', N'Networking', 159.00, 10),
       (N'Cat6 Cable 30m', N'Networking', 21.00, 40),
       (N'Patch Panel', N'Networking', 69.00, 10),
       (N'UPS Battery Backup', N'Networking', 189.00, 8);

;WITH n AS (SELECT TOP (250) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i FROM sys.all_objects),
first_names AS (SELECT * FROM (VALUES (0, N'Olivia'), (1, N'Liam'), (2, N'Emma'), (3, N'Noah'), (4, N'Ava'), (5, N'Elijah'), (6, N'Sophia'),
        (7, N'James'), (8, N'Isabella'), (9, N'Lucas'), (10, N'Mia'), (11, N'Mateo'), (12, N'Amelia'), (13, N'Ethan'), (14, N'Harper'),
        (15, N'Aiden'), (16, N'Priya'), (17, N'Kenji'), (18, N'Fatima'), (19, N'Diego')) AS f (k, name)),
last_names AS (SELECT * FROM (VALUES (0, N'Smith'), (1, N'Johnson'), (2, N'Williams'), (3, N'Brown'), (4, N'Garcia'), (5, N'Miller'),
        (6, N'Davis'), (7, N'Rodriguez'), (8, N'Martinez'), (9, N'Lopez'), (10, N'Wilson'), (11, N'Anderson'), (12, N'Thomas'),
        (13, N'Taylor'), (14, N'Moore'), (15, N'Patel'), (16, N'Nguyen'), (17, N'Kim'), (18, N'Okafor'), (19, N'Rossi')) AS l (k, name)),
cities AS (SELECT * FROM (VALUES (0, N'Seattle', N'West'), (1, N'Portland', N'West'), (2, N'San Francisco', N'West'), (3, N'Denver', N'West'),
        (4, N'Dallas', N'South'), (5, N'Houston', N'South'), (6, N'Atlanta', N'South'), (7, N'Miami', N'South'),
        (8, N'Chicago', N'Central'), (9, N'Minneapolis', N'Central'), (10, N'St. Louis', N'Central'),
        (11, N'New York', N'East'), (12, N'Boston', N'East'), (13, N'Philadelphia', N'East'), (14, N'Detroit', N'North')) AS c (k, City, Region))
INSERT dbo.Customers (CustomerName, Email, City, Region, CreatedDate)
SELECT f.name + N' ' + l.name,
       LOWER(f.name + N'.' + l.name) + CAST(n.i AS nvarchar(10)) + N'@example.com',
       c.City,
       c.Region,
       DATEADD(DAY, -(460 + (n.i * 7) % 500), CAST(GETDATE() AS datetime2(0)))
FROM n
JOIN first_names f ON f.k = n.i % 20
JOIN last_names l ON l.k = (n.i / 20) % 20
JOIN cities c ON c.k = n.i % 15;

INSERT dbo.Inventory (ProductId, WarehouseId, AvailableQuantity, ReservedQuantity, LastUpdated)
SELECT p.ProductId,
       w.WarehouseId,
       ABS(CHECKSUM(p.ProductId * 31 + w.WarehouseId * 17)) % 180,
       ABS(CHECKSUM(p.ProductId * 7 + w.WarehouseId)) % 15,
       DATEADD(HOUR, -((p.ProductId * 13 + w.WarehouseId * 29) % 720), CAST(GETDATE() AS datetime2(0)))
FROM dbo.Products p
CROSS JOIN dbo.Warehouses w
WHERE (p.ProductId + w.WarehouseId) % 4 <> 0;

-- 6,000 orders over the last ~15 months; the first 20 are placed within the last hour ("today").
;WITH n AS (SELECT TOP (6000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i FROM sys.all_objects a CROSS JOIN sys.all_objects b),
dated AS (
    SELECT n.i,
           CASE WHEN n.i <= 20 THEN DATEADD(MINUTE, -(n.i * 3), CAST(GETDATE() AS datetime2(0)))
                ELSE DATEADD(MINUTE, -((n.i * 7919) % (450 * 1440)) - 90, CAST(GETDATE() AS datetime2(0))) END AS OrderDate
    FROM n),
statused AS (
    SELECT d.i, d.OrderDate,
           CASE WHEN DATEDIFF(DAY, d.OrderDate, GETDATE()) < 2 THEN CASE WHEN d.i % 2 = 0 THEN N'Pending' ELSE N'Processing' END
                WHEN d.i % 25 = 0 THEN N'Cancelled'
                WHEN DATEDIFF(DAY, d.OrderDate, GETDATE()) < 7 THEN CASE d.i % 3 WHEN 0 THEN N'Shipped' WHEN 1 THEN N'Processing' ELSE N'Pending' END
                WHEN d.i % 40 = 1 THEN N'Pending'
                WHEN DATEDIFF(DAY, d.OrderDate, GETDATE()) < 14 AND d.i % 2 = 0 THEN N'Shipped'
                ELSE N'Delivered' END AS OrderStatus
    FROM dated d)
INSERT dbo.Orders (CustomerId, OrderDate, OrderStatus, TotalAmount, WarehouseId, ShippedDate)
SELECT 1 + (s.i * 37) % 230,                      -- customers 231-250 never order
       s.OrderDate,
       s.OrderStatus,
       0,
       1 + s.i % 5,
       CASE WHEN s.OrderStatus IN (N'Shipped', N'Delivered') THEN
            CASE WHEN DATEADD(HOUR, 4 + (s.i * 11) % 96, s.OrderDate) > GETDATE() THEN CAST(GETDATE() AS datetime2(0))
                 ELSE DATEADD(HOUR, 4 + (s.i * 11) % 96, s.OrderDate) END
       END
FROM statused s
ORDER BY s.OrderDate;

INSERT dbo.OrderDetails (OrderId, ProductId, Quantity, UnitPrice)
SELECT o.OrderId,
       p.ProductId,
       1 + (o.OrderId + k.k * 3) % 5,
       p.UnitPrice
FROM dbo.Orders o
CROSS JOIN (VALUES (1), (2), (3), (4)) AS k (k)
JOIN dbo.Products p ON p.ProductId = 1 + ((o.OrderId * 13 + k.k * 7) % 40)
WHERE k.k <= 1 + o.OrderId % 4;

UPDATE o
SET TotalAmount = t.Total
FROM dbo.Orders o
JOIN (SELECT OrderId, SUM(LineTotal) AS Total FROM dbo.OrderDetails GROUP BY OrderId) t ON t.OrderId = o.OrderId;

INSERT dbo.Payments (OrderId, PaymentDate, PaymentAmount, PaymentStatus)
SELECT o.OrderId,
       CASE WHEN DATEADD(MINUTE, 30 + o.OrderId % 600, o.OrderDate) > GETDATE() THEN CAST(GETDATE() AS datetime2(0))
            ELSE DATEADD(MINUTE, 30 + o.OrderId % 600, o.OrderDate) END,
       o.TotalAmount,
       CASE WHEN o.OrderStatus = N'Cancelled' THEN N'Refunded'
            WHEN o.OrderId % 50 = 0 THEN N'Failed'
            WHEN o.OrderStatus IN (N'Pending', N'Processing') AND o.OrderId % 3 = 0 THEN N'Pending'
            ELSE N'Completed' END
FROM dbo.Orders o
WHERE NOT (o.OrderStatus = N'Pending' AND o.OrderId % 3 <> 0);

-- Retry payments that failed.
INSERT dbo.Payments (OrderId, PaymentDate, PaymentAmount, PaymentStatus)
SELECT p.OrderId, DATEADD(HOUR, 2, p.PaymentDate), p.PaymentAmount, N'Completed'
FROM dbo.Payments p
WHERE p.PaymentStatus = N'Failed' AND DATEADD(HOUR, 2, p.PaymentDate) <= GETDATE();
GO

/* ---------------------------------------------- Least-privilege read-only principal

   The application must connect with a dedicated login that can only SELECT. SQL Server enforces this independently of
   the application's AST validation (defence in depth). Never use sysadmin, db_owner or db_datawriter credentials.
*/
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'aiqg_reader')
    CREATE LOGIN aiqg_reader WITH PASSWORD = N'$(ReaderPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = AIQueryDemoDB;
ELSE
    ALTER LOGIN aiqg_reader WITH PASSWORD = N'$(ReaderPassword)';
GO
USE AIQueryDemoDB;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'aiqg_reader')
    CREATE USER aiqg_reader FOR LOGIN aiqg_reader WITH DEFAULT_SCHEMA = dbo;
GO
GRANT SELECT ON SCHEMA::dbo TO aiqg_reader;
GRANT VIEW DEFINITION ON SCHEMA::dbo TO aiqg_reader;   -- schema discovery
GRANT SHOWPLAN TO aiqg_reader;                          -- estimated execution plans
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE, REFERENCES ON SCHEMA::dbo TO aiqg_reader;
DENY CREATE TABLE, CREATE VIEW, CREATE PROCEDURE, CREATE FUNCTION TO aiqg_reader;
-- Personal data: even if an administrator later enables Customers.Email in the application,
-- SQL Server itself keeps it unreadable for this principal.
DENY SELECT ON dbo.Customers (Email) TO aiqg_reader;
GO

/* ------------------------------------------------------------- Summary */
SELECT (SELECT COUNT(*) FROM dbo.Customers)    AS Customers,
       (SELECT COUNT(*) FROM dbo.Products)     AS Products,
       (SELECT COUNT(*) FROM dbo.Warehouses)   AS Warehouses,
       (SELECT COUNT(*) FROM dbo.Inventory)    AS InventoryRows,
       (SELECT COUNT(*) FROM dbo.Orders)       AS Orders,
       (SELECT COUNT(*) FROM dbo.OrderDetails) AS OrderLines,
       (SELECT COUNT(*) FROM dbo.Payments)     AS Payments;
GO
