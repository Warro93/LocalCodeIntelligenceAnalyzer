CREATE TYPE dbo.OrderLineList AS TABLE
(
    ProductCode NVARCHAR (50)   NOT NULL,
    Quantity    INT             NOT NULL,
    UnitPrice   DECIMAL (18, 2) NOT NULL
);
GO
