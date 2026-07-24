-- Bu dosyayı AdminLicenseConsole.sql dosyasından SONRA, SQL Server Agent çalışan
-- SQL Server Standard/Developer/Enterprise örneğinde çalıştırın.
USE msdb;
GO

IF EXISTS (SELECT 1 FROM msdb.dbo.sysjobs WHERE name = N'Netvium - Premium Süre Kontrolü')
    EXEC msdb.dbo.sp_delete_job @job_name = N'Netvium - Premium Süre Kontrolü';
GO

EXEC msdb.dbo.sp_add_job
    @job_name = N'Netvium - Premium Süre Kontrolü',
    @enabled = 1,
    @description = N'Süresi dolan Netvium lisanslarını FREE plana geçirir.';

EXEC msdb.dbo.sp_add_jobstep
    @job_name = N'Netvium - Premium Süre Kontrolü',
    @step_name = N'Süresi dolan lisansları güncelle',
    @subsystem = N'TSQL',
    @database_name = N'NetviumIdentity',
    @command = N'EXEC dbo.ExpireNetviumLicenses;',
    @on_success_action = 1,
    @on_fail_action = 2;

EXEC msdb.dbo.sp_add_schedule
    @schedule_name = N'Netvium - Saatlik Lisans Kontrolü',
    @enabled = 1,
    @freq_type = 4,
    @freq_interval = 1,
    @freq_subday_type = 8,
    @freq_subday_interval = 1,
    @active_start_time = 000000;

EXEC msdb.dbo.sp_attach_schedule
    @job_name = N'Netvium - Premium Süre Kontrolü',
    @schedule_name = N'Netvium - Saatlik Lisans Kontrolü';

EXEC msdb.dbo.sp_add_jobserver
    @job_name = N'Netvium - Premium Süre Kontrolü';
GO
