using Netvium.Helpers;
using Netvium.Models;
using Netvium.Services;
using Netvium.ViewModels;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class GlobalExceptionHandlingTests
{
    [Fact]
    public void Logger_format_redacts_password_token_api_key_and_sql_connection_string()
    {
        const string password = "P@ssw0rd-Secret";
        const string token = "eyJhbGciOiJIUzI1NiJ9.abcdefghijklmnop.qrstuvwxyz123456";
        const string apiKey = "sk-secret-api-value";
        const string sqlSecret = "SqlPassword-Secret";
        const string connectionString =
            "Server=sql01;Database=Netvium;User Id=sa;Password=SqlPassword-Secret;Encrypt=True";

        Exception exception;
        try
        {
            throw new InvalidOperationException(
                $"Password={password}; Authorization: Bearer {token}; connection={connectionString}");
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        var line = AppLogger.FormatForTesting(
            $"api_key={apiKey}; token={token}; sql={connectionString}",
            exception);

        Assert.DoesNotContain(password, line, StringComparison.Ordinal);
        Assert.DoesNotContain(token, line, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, line, StringComparison.Ordinal);
        Assert.DoesNotContain(sqlSecret, line, StringComparison.Ordinal);
        Assert.DoesNotContain("Server=sql01", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[REDACTED]", line, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), line, StringComparison.Ordinal);
        Assert.Contains(nameof(Logger_format_redacts_password_token_api_key_and_sql_connection_string), line, StringComparison.Ordinal);
    }

    [Fact]
    public void Dispatcher_exceptions_are_classified_without_blindly_marking_unknown_faults_recoverable()
    {
        Assert.Equal(
            GlobalExceptionKind.RecoverableUi,
            GlobalExceptionHandler.Classify(
                new InvalidOperationException("recoverable"),
                GlobalExceptionOrigin.Dispatcher));

        Assert.Equal(
            GlobalExceptionKind.Critical,
            GlobalExceptionHandler.Classify(
                new NullReferenceException("programming fault"),
                GlobalExceptionOrigin.Dispatcher));

        Assert.Equal(
            GlobalExceptionKind.Critical,
            GlobalExceptionHandler.Classify(
                new OutOfMemoryException(),
                GlobalExceptionOrigin.UnobservedTask));
    }

    [Fact]
    public async Task Background_task_exception_is_observed_and_reported()
    {
        var expected = new IOException("background failed");
        Exception? reported = null;

        await GlobalExceptionHandler.ObserveAsync(
            Task.FromException(expected),
            "Test.Background",
            testReporter: ex => reported = ex);

        Assert.Same(expected, reported);
    }

    [Fact]
    public async Task Async_command_reports_exception_resets_state_and_can_run_again()
    {
        var invocationCount = 0;
        Exception? reported = null;
        var expected = new InvalidOperationException("command failed");
        var command = new AsyncRelayCommand(
            _ =>
            {
                invocationCount++;
                return Task.FromException(expected);
            },
            onException: ex => reported = ex);

        await command.ExecuteAsync(null);

        Assert.Same(expected, reported);
        Assert.True(command.CanExecute(null));

        await command.ExecuteAsync(null);

        Assert.Equal(2, invocationCount);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Repeated_notification_is_suppressed_inside_window()
    {
        var throttle = new ExceptionNotificationThrottle(TimeSpan.FromSeconds(30));
        var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(throttle.ShouldNotify("same-error", now));
        Assert.False(throttle.ShouldNotify("same-error", now.AddSeconds(10)));
        Assert.True(throttle.ShouldNotify("different-error", now.AddSeconds(10)));
        Assert.True(throttle.ShouldNotify("same-error", now.AddSeconds(31)));
    }

    [Fact]
    public void Audit_log_entry_is_redacted_before_serialization()
    {
        const string password = "AuditPassword-Secret";
        const string token = "AuditToken-Secret";
        const string connectionString =
            "Server=audit-db;Database=Netvium;User Id=sa;Password=AuditSql-Secret";
        var entry = new AuditLogEntry
        {
            Message = $"password={password}; token={token}",
            ErrorMessage = $"connection={connectionString}",
            DetailsJson = $"{{\"apiKey\":\"AuditApi-Secret\"}}"
        };

        var redacted = AuditLogService.RedactForTesting(entry);

        var combined = $"{redacted.Message}|{redacted.ErrorMessage}|{redacted.DetailsJson}";
        Assert.DoesNotContain(password, combined, StringComparison.Ordinal);
        Assert.DoesNotContain(token, combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditSql-Secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("AuditApi-Secret", combined, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", combined, StringComparison.Ordinal);
    }
}
