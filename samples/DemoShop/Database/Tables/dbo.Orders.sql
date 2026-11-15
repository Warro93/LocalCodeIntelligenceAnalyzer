CREATE TABLE dbo.Orders
(
    OrderId      INT             IDENTITY (1, 1) NOT NULL,
    CustomerId   INT             NOT NULL,
    OrderDate    DATETIME2 (3)   NOT NULL CONSTRAINT DF_Orders_OrderDate DEFAULT (SYSUTCDATETIME()),
    Status       TINYINT         NOT NULL CONSTRAINT DF_Orders_Status DEFAULT (0),
    Total        DECIMAL (18, 2) NOT NULL CONSTRAINT DF_Orders_Total DEFAULT (0),
    CancelReason NVARCHAR (400)  NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderId),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers (CustomerId),
    CONSTRAINT CK_Orders_Status CHECK (Status IN (0, 1, 2))
);
GO

CREATE NONCLUSTERED INDEX IX_Orders_CustomerId ON dbo.Orders (CustomerId) INCLUDE (Status, Total);
GO
