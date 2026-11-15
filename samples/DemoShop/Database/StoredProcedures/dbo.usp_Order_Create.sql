CREATE PROCEDURE dbo.usp_Order_Create
    @CustomerId INT,
    @LinesJson  NVARCHAR (MAX),
    @OrderId    INT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE CustomerId = @CustomerId AND IsActive = 1)
            THROW 50001, N'Cliente inesistente o non attivo.', 1;

        INSERT INTO dbo.Orders (CustomerId, Status, Total)
        VALUES (@CustomerId, 0, 0);

        SET @OrderId = CAST(SCOPE_IDENTITY() AS INT);

        INSERT INTO dbo.OrderLines (OrderId, ProductCode, Quantity, UnitPrice)
        SELECT @OrderId, j.ProductCode, j.Quantity, j.UnitPrice
        FROM OPENJSON(@LinesJson)
             WITH (ProductCode NVARCHAR (50)   '$.ProductCode',
                   Quantity    INT             '$.Quantity',
                   UnitPrice   DECIMAL (18, 2) '$.UnitPrice') AS j;

        UPDATE dbo.Orders
        SET Total = dbo.fn_OrderTotal(@OrderId)
        WHERE OrderId = @OrderId;

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO
