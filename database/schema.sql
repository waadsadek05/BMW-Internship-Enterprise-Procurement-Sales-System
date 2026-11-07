CREATE DATABASE EnterpriseProcurementSalesDb;
GO

USE EnterpriseProcurementSalesDb;
GO

CREATE TABLE Branches (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    Location NVARCHAR(250) NULL
);

CREATE TABLE Warehouses (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BranchId INT NOT NULL,
    Name NVARCHAR(150) NOT NULL,
    Location NVARCHAR(250) NULL,
    CONSTRAINT FK_Warehouses_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id) ON DELETE CASCADE
);

CREATE TABLE ApplicationUsers (
    Id NVARCHAR(450) PRIMARY KEY,
    FullName NVARCHAR(150) NOT NULL,
    Email NVARCHAR(150) NOT NULL,
    Username NVARCHAR(100) NULL,
    Password NVARCHAR(100) NULL,
    IsActive BIT NOT NULL CONSTRAINT DF_ApplicationUsers_IsActive DEFAULT 1,
    BranchId INT NULL,
    CONSTRAINT FK_Users_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
);

CREATE TABLE Roles (
    Id NVARCHAR(450) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL
);

CREATE TABLE UserRoles (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    UserId NVARCHAR(450) NOT NULL,
    RoleId NVARCHAR(450) NOT NULL,
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES ApplicationUsers(Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES Roles(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_User_Role UNIQUE (UserId, RoleId)
);

CREATE TABLE Categories (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(100) NOT NULL
);

CREATE TABLE Products (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    SKU NVARCHAR(50) NOT NULL UNIQUE,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CategoryId INT NOT NULL,
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId) REFERENCES Categories(Id)
);

CREATE TABLE BranchInventory (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    WarehouseId INT NOT NULL,
    ProductId INT NOT NULL,
    StockQuantity INT NOT NULL DEFAULT 0,
    CONSTRAINT FK_Inventory_Warehouses FOREIGN KEY (WarehouseId) REFERENCES Warehouses(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Inventory_Products FOREIGN KEY (ProductId) REFERENCES Products(Id),
    CONSTRAINT UQ_Warehouse_Product UNIQUE (WarehouseId, ProductId)
);

CREATE TABLE Vendors (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    ContactInfo NVARCHAR(250) NULL
);

CREATE TABLE PurchaseOrders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    VendorId INT NOT NULL,
    BranchId INT NOT NULL,
    OrderDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending', -- Pending, Completed, Cancelled
    CONSTRAINT FK_PO_Vendors FOREIGN KEY (VendorId) REFERENCES Vendors(Id),
    CONSTRAINT FK_PO_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id)
);

CREATE TABLE PurchaseOrderItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitCost DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_POItems_PO FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id) ON DELETE CASCADE,
    CONSTRAINT FK_POItems_Products FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE TABLE VendorInvoices (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PurchaseOrderId INT NOT NULL,
    InvoiceNumber NVARCHAR(50) NOT NULL,
    InvoiceDate DATETIME2 NOT NULL,
    TotalAmount DECIMAL(18,2) NOT NULL,
    Status NVARCHAR(50) NOT NULL DEFAULT 'Unpaid',
    CONSTRAINT FK_VendorInvoices_PO FOREIGN KEY (PurchaseOrderId) REFERENCES PurchaseOrders(Id)
);

CREATE TABLE Customers (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    Name NVARCHAR(150) NOT NULL,
    Phone NVARCHAR(50) NULL
);

CREATE TABLE SalesOrders (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CustomerId INT NOT NULL,
    BranchId INT NOT NULL,
    UserId NVARCHAR(450) NOT NULL,
    OrderDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Status NVARCHAR(50) NOT NULL DEFAULT 'Pending',
    CONSTRAINT FK_SO_Customers FOREIGN KEY (CustomerId) REFERENCES Customers(Id),
    CONSTRAINT FK_SO_Branches FOREIGN KEY (BranchId) REFERENCES Branches(Id),
    CONSTRAINT FK_SO_Users FOREIGN KEY (UserId) REFERENCES ApplicationUsers(Id)
);

CREATE TABLE SalesOrderItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SalesOrderId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_SOItems_SO FOREIGN KEY (SalesOrderId) REFERENCES SalesOrders(Id) ON DELETE CASCADE,
    CONSTRAINT FK_SOItems_Products FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE TABLE SalesInvoices (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SalesOrderId INT NOT NULL,
    InvoiceNumber NVARCHAR(50) NOT NULL UNIQUE,
    InvoiceDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    TotalAmount DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_SalesInvoices_SO FOREIGN KEY (SalesOrderId) REFERENCES SalesOrders(Id)
);

CREATE TABLE InvoiceItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SalesInvoiceId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_InvoiceItems_Invoice FOREIGN KEY (SalesInvoiceId) REFERENCES SalesInvoices(Id) ON DELETE CASCADE,
    CONSTRAINT FK_InvoiceItems_Products FOREIGN KEY (ProductId) REFERENCES Products(Id)
);

CREATE TABLE CreditNotes (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    SalesInvoiceId INT NOT NULL,
    ReturnDate DATETIME2 NOT NULL DEFAULT GETDATE(),
    Reason NVARCHAR(250) NULL,
    RefundAmount DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_CreditNotes_Invoice FOREIGN KEY (SalesInvoiceId) REFERENCES SalesInvoices(Id)
);

CREATE TABLE CreditNoteItems (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    CreditNoteId INT NOT NULL,
    ProductId INT NOT NULL,
    Quantity INT NOT NULL,
    UnitPrice DECIMAL(18,2) NOT NULL,
    CONSTRAINT FK_CNItems_CN FOREIGN KEY (CreditNoteId) REFERENCES CreditNotes(Id) ON DELETE CASCADE,
    CONSTRAINT FK_CNItems_Products FOREIGN KEY (ProductId) REFERENCES Products(Id)
);
GO

-- Data is NOT seeded here. Roles, Categories, Products, and all other test/demo
-- data are populated separately by generate_bmw_test_data.py, which introspects
-- this schema (via INFORMATION_SCHEMA / sys.foreign_keys) and bulk-generates
-- relationally valid mock data in the correct dependency order.
-- Run this schema.sql first to create the empty database, then run
-- generate_bmw_test_data.py to fill it.