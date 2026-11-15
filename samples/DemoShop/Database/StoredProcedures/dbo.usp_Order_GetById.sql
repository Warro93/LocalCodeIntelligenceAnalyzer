CREATE PROCEDURE dbo.usp_Order_GetById
    @OrderId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.OrderId, s.CustomerId, s.CustomerName, s.Status, s.Total
    FROM dbo.vw_OrderSummary AS s
    WHERE s.OrderId = @OrderId;

    SELECT l.ProductCode, l.Quantity, l.UnitPrice
    FROM dbo.OrderLines AS l
    WHERE l.OrderId = @OrderId
    ORDER BY l.OrderLineId;
END;
GO
