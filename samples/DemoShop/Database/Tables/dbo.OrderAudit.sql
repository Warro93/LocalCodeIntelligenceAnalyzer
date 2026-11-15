CREATE TABLE dbo.OrderAudit
(
    AuditId     BIGINT        IDENTITY (1, 1) NOT NULL,
    OrderId     INT           NOT NULL,
    OldStatus   TINYINT       NULL,
    NewStatus   TINYINT       NOT NULL,
    ChangedAt   DATETIME2 (3) NOT NULL CONSTRAINT DF_OrderAudit_ChangedAt DEFAULT (SYSUTCDATETIME()),
    CONSTRAINT PK_OrderAudit PRIMARY KEY CLUSTERED (AuditId)
);
GO
