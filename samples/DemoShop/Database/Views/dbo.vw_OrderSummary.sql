CREATE VIEW dbo.vw_OrderSummary
AS
SELECT o.OrderId,
       o.CustomerId,
       c.Name AS CustomerName,
       o.Status,
       o.Total,
       o.OrderDate
FROM dbo.Orders AS o
INNER JOIN dbo.Customers AS c ON c.CustomerId = o.CustomerId;
GO
