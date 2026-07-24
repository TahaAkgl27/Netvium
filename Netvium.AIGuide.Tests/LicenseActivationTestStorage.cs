using System;
using System.IO;
using Netvium.Services.Licensing;

namespace Netvium.AIGuide.Tests;

internal static class LicenseActivationTestStorage
{
    public static IDisposable Begin()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"netvium-license-activation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return new CleanupScope(LicenseStorage.UseTestDirectory(directory), directory);
    }

    private sealed class CleanupScope : IDisposable
    {
        private readonly IDisposable _scope;
        private readonly string _directory;
        public CleanupScope(IDisposable scope, string directory) { _scope = scope; _directory = directory; }
        public void Dispose()
        {
            _scope.Dispose();
            try { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); } catch { }
        }
    }
}