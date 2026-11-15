# DemoShop — progetto dimostrativo

Piccolo progetto ASP.NET Core usato come **dato di test** dalla suite automatica e come esempio da
analizzare con Local Code Intelligence Analyzer. Non fa parte della solution e non viene compilato in CI.

Contiene un flusso completo "creazione ordine":

```
OrdersController.Create            (Controllers/OrdersController.cs)
  -> IOrderService.CreateOrderAsync (Services/OrderService.cs)
  -> IOrderRepository.InsertOrderAsync (Repositories/OrderRepository.cs)
  -> dbo.usp_Order_Create          (Database/StoredProcedures/dbo.usp_Order_Create.sql)
       -> dbo.Customers, dbo.Orders, dbo.OrderLines, dbo.fn_OrderTotal
```

- Dependency injection in `Program.cs` (`AddScoped`, `AddSingleton`).
- Accesso dati ADO.NET con `CommandType.StoredProcedure` e una query SQL in stringa.
- Script T-SQL di tabelle, foreign key, indici, vista, funzione, trigger, tipo tabellare e stored procedure.
- `appsettings.json` contiene una connection string con una password **fittizia** (serve a verificare
  l'oscuramento dei segreti nell'esportazione). Il file è elencato in `scripts/secret-scan-allowlist.txt`.
