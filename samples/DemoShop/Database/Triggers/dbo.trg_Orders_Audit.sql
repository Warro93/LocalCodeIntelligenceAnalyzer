CREATE TRIGGER dbo.trg_Orders_Audit
ON dbo.Orders
AFTER UPDATE
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.OrderAudit (OrderId, OldStatus, NewStatus)
    SELECT i.OrderId, d.Status, i.Status
    FROM inserted AS i
    INNER JOIN deleted AS d ON d.OrderId = i.OrderId
    WHERE i.Status <> d.Status;
END;
GO
