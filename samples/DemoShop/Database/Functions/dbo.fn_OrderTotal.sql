CREATE FUNCTION dbo.fn_OrderTotal (@OrderId INT)
RETURNS DECIMAL (18, 2)
AS
BEGIN
    DECLARE @Total DECIMAL (18, 2);

    SELECT @Total = COALESCE(SUM(l.Quantity * l.UnitPrice), 0)
    FROM dbo.OrderLines AS l
    WHERE l.OrderId = @OrderId;

    RETURN @Total;
END;
GO
