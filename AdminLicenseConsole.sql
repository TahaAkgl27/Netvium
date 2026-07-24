IF OBJECT_ID(N'dbo.ActivationTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ActivationTokens
    (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        TokenHash CHAR(64) NOT NULL UNIQUE,
        PlanId INT NOT NULL,
        PlanDurationDays INT NOT NULL,
        CreatedByUserId UNIQUEIDENTIFIER NOT NULL,
        CreatedAtUtc DATETIME2 NOT NULL,
        ExpiresAtUtc DATETIME2 NULL,
        RedeemedAtUtc DATETIME2 NULL,
        RedeemedByUserId UNIQUEIDENTIFIER NULL,
        RevokedAtUtc DATETIME2 NULL,
        Note NVARCHAR(250) NULL,
        CONSTRAINT FK_ActivationTokens_Plans FOREIGN KEY (PlanId) REFERENCES dbo.Plans(Id)
    );
END;

IF COL_LENGTH(N'dbo.ActivationTokens', N'PlanDurationDays') IS NULL
    ALTER TABLE dbo.ActivationTokens ADD PlanDurationDays INT NOT NULL CONSTRAINT DF_ActivationTokens_PlanDurationDays DEFAULT (30);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ActivationTokens_Status' AND object_id = OBJECT_ID(N'dbo.ActivationTokens'))
    CREATE INDEX IX_ActivationTokens_Status ON dbo.ActivationTokens (RedeemedAtUtc, RevokedAtUtc);

IF OBJECT_ID(N'dbo.AccountUsageEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountUsageEvents
    (
        Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        UserId UNIQUEIDENTIFIER NULL,
        EventType VARCHAR(80) NOT NULL,
        Detail NVARCHAR(500) NULL,
        Quantity INT NOT NULL CONSTRAINT DF_AccountUsageEvents_Quantity DEFAULT (1),
        CreatedAtUtc DATETIME2 NOT NULL
    );
    CREATE INDEX IX_AccountUsageEvents_User_Created ON dbo.AccountUsageEvents (UserId, CreatedAtUtc);
END;
GO

CREATE OR ALTER PROCEDURE dbo.ExpireNetviumLicenses
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @NowUtc DATETIME2 = SYSUTCDATETIME();
    DECLARE @FreePlanId INT = (SELECT TOP (1) Id FROM dbo.Plans WHERE PlanCode = 'FREE' AND IsActive = 1);

    UPDATE dbo.UserLicenses
    SET Status = 'EXPIRED', CancelledAtUtc = @NowUtc, UpdatedAtUtc = @NowUtc
    WHERE Status = 'ACTIVE' AND ExpiresAtUtc IS NOT NULL AND ExpiresAtUtc <= @NowUtc;

    INSERT INTO dbo.UserLicenses (Id, UserId, PlanId, Status, StartsAtUtc, ExpiresAtUtc, CreatedAtUtc, UpdatedAtUtc)
    SELECT NEWID(), u.Id, @FreePlanId, 'ACTIVE', @NowUtc, NULL, @NowUtc, @NowUtc
    FROM dbo.Users u
    WHERE @FreePlanId IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM dbo.UserLicenses ul WHERE ul.UserId = u.Id AND ul.Status = 'ACTIVE');
END;
GO

-- SQL Server Agent etkinse dbo.ExpireNetviumLicenses prosedürünü her saat çalıştıran bir Job oluşturun.
-- SQL Server Express'te Agent bulunmaz; Windows Görev Zamanlayıcı ile
-- `sqlcmd -Q "EXEC dbo.ExpireNetviumLicenses"` komutunu saatlik çalıştırın.
