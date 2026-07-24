using System.Security.Cryptography;
using System.Text;
using Netvium.Models;
using Netvium.Services.Licensing;
using Xunit;

namespace Netvium.AIGuide.Tests;

[Collection("LicensingState")]
public sealed class LicenseStorageIsolationTests
{
    [Fact]
    public void Save_and_load_use_a_unique_test_path()
    {
        using var scope = TestScope.Create();
        var record = CreateRecord();

        Assert.True(LicenseStorage.Save(record));
        Assert.True(LicenseStorage.TryLoad(out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal(record.ActivationHash, loaded!.ActivationHash);
        Assert.Equal(Path.Combine(scope.Directory, "license.dat"), LicenseStorage.GetLicenseFilePath());
    }

    [Fact]
    public void Save_failure_is_fail_closed_and_activation_is_not_successful()
    {
        using var scope = TestScope.Create();
        var blockedPath = Path.Combine(scope.Directory, "license.dat");
        Directory.CreateDirectory(blockedPath);
        var record = CreateRecord();

        Assert.False(LicenseStorage.Save(record));
        Assert.False(LicenseActivationService.Instance.Activate("NTVM-PRM-7Q4X-L2PK").Success);
        Assert.Empty(Directory.GetFiles(scope.Directory, ".license.dat.*.tmp"));
    }

    [Fact]
    public void Corrupt_file_fails_closed()
    {
        using var scope = TestScope.Create();
        File.WriteAllText(Path.Combine(scope.Directory, "license.dat"), "{not-json", Encoding.UTF8);

        Assert.False(LicenseStorage.TryLoad(out var record));
        Assert.Null(record);
    }

    [Fact]
    public async Task Parallel_saves_are_serialized_and_leave_one_valid_record()
    {
        using var scope = TestScope.Create();
        var records = Enumerable.Range(0, 16).Select(_ => CreateRecord()).ToArray();

        var results = await Task.WhenAll(records.Select(record => Task.Run(() => LicenseStorage.Save(record))));

        Assert.All(results, Assert.True);
        Assert.True(LicenseStorage.TryLoad(out var loaded));
        Assert.NotNull(loaded);
        Assert.Empty(Directory.GetFiles(scope.Directory, ".license.dat.*.tmp"));
    }

    [Fact]
    public void Production_license_metadata_is_unchanged()
    {
        var productionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Netvium",
            "license.dat");
        var before = ReadMetadata(productionPath);

        using (var scope = TestScope.Create())
        {
            Assert.True(LicenseStorage.Save(CreateRecord()));
            Assert.NotEqual(productionPath, LicenseStorage.GetLicenseFilePath());
        }

        var after = ReadMetadata(productionPath);
        Assert.Equal(before, after);
    }

    private static LicenseRecord CreateRecord() => new()
    {
        Plan = LicensingPlanTier.Premium,
        ActivatedAtUtc = DateTime.UtcNow,
        ActivationHash = "test-hash",
        Signature = "test-signature"
    };

    private static string ReadMetadata(string path)
    {
        if (!File.Exists(path))
            return "missing";
        var info = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(SHA256.HashData(stream));
        return $"{info.Length}|{info.LastWriteTimeUtc.Ticks}|{hash}";
    }

    private sealed class TestScope : IDisposable
    {
        public string Directory { get; }
        private readonly IDisposable _storageScope;

        private TestScope(string directory)
        {
            Directory = directory;
            _storageScope = LicenseStorage.UseTestDirectory(directory);
        }

        public static TestScope Create()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"netvium-license-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new TestScope(directory);
        }

        public void Dispose()
        {
            _storageScope.Dispose();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch
            {
                // Test cleanup must not fall back to the production path.
            }
        }
    }
}