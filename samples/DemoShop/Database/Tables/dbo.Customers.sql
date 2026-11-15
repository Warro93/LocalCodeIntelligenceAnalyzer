CREATE TABLE dbo.Customers
(
    CustomerId  INT            IDENTITY (1, 1) NOT NULL,
    Name        NVARCHAR (200) NOT NULL,
    Email       NVARCHAR (320) NULL,
    IsActive    BIT            NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT (1),
    CreatedAt   DATETIME2 (3)  NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (CustomerId),
    CONSTRAINT UQ_Customers_Email UNIQUE (Email)
);
GO
