CREATE TABLE dbo.OrderLines
(
    OrderLineId INT             IDENTITY (1, 1) NOT NULL,
    OrderId     INT             NOT NULL,
    ProductCode NVARCHAR (50)   NOT NULL,
    Quantity    INT             NOT NULL,
    UnitPrice   DECIMAL (18, 2) NOT NULL,
    CONSTRAINT PK_OrderLines PRIMARY KEY CLUSTERED (OrderLineId),
    CONSTRAINT FK_OrderLines_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (OrderId) ON DELETE CASCADE,
    CONSTRAINT CK_OrderLines_Quantity CHECK (Quantity > 0)
);
GO
