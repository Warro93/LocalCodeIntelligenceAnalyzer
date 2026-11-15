CREATE PROCEDURE dbo.usp_Order_Cancel
    @OrderId INT,
    @Reason  NVARCHAR (400)
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.Orders
    SET Status = 2,
        CancelReason = @Reason
    WHERE OrderId = @OrderId
      AND Status <> 2;

    SELECT @@ROWCOUNT AS AffectedRows;
END;
GO
