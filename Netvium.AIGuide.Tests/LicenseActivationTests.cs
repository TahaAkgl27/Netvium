using System;
using System.IO;
using Netvium.Models;
using Netvium.Services;
using Netvium.Services.Licensing;
using Xunit;

namespace Netvium.AIGuide.Tests;

[CollectionDefinition("LicensingState", DisableParallelization = true)]
public sealed class LicensingStateCollection;

[Collection("LicensingState")]
public sealed class LicenseActivationTests
{
    [Theory]
    [InlineData("NTVM-PRM-7Q4X-L2PK", LicensingPlanTier.Premium)]
    [InlineData("ntvm-prm-7q4x-l2pk", LicensingPlanTier.Premium)]
    [InlineData("  NTVM-PRM-G2LP-9XWT  ", LicensingPlanTier.Premium)]
    [InlineData("NTVM-PLUS-7Q4X-L2PK", LicensingPlanTier.PremiumPlus)]
    [InlineData("ntvm-plus-7q4x-l2pk", LicensingPlanTier.PremiumPlus)]
    [InlineData("NTVM-PLUS-G2LP-9XWT", LicensingPlanTier.PremiumPlus)]
    public void Valid_codes_are_accepted_for_correct_tier(string code, LicensingPlanTier expectedPlan)
    {
        Assert.True(LicenseValidator.TryValidateActivationCode(code, out var plan, out var hash));
        Assert.Equal(expectedPlan, plan);
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Theory]
    [InlineData("NTVM-XXXX-YYYY-ZZZZ")]
    [InlineData("NTVM-PRM-7Q4X-L2PKX")]
    [InlineData("NTVM-PLUS-7Q4X-L2PKX")]
    public void Invalid_codes_are_rejected(string code)
    {
        Assert.False(LicenseValidator.TryValidateActivationCode(code, out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Enter Activation Code")]
    public void Empty_activation_code_returns_prompt_without_changing_license(string code)
    {
        LicensingUiState.Instance.ApplyRestoredLicense(LicensingPlanTier.Premium);
        try
        {
            var result = LicenseActivationService.Instance.Activate(code);
            Assert.False(result.Success);
            Assert.Equal(LicenseConstants.EmptyActivationMessage, result.Message);
            Assert.True(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.False(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void Invalid_activation_code_does_not_clear_existing_license()
    {
        LicensingUiState.Instance.ApplyRestoredLicense(LicensingPlanTier.PremiumPlus);
        try
        {
            var result = LicenseActivationService.Instance.Activate("NTVM-XXXX-YYYY-ZZZZ");
            Assert.False(result.Success);
            Assert.Equal(LicenseConstants.InvalidActivationMessage, result.Message);
            Assert.True(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void Premium_code_unlocks_premium_only()
    {
        using var storageScope = LicenseActivationTestStorage.Begin();
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            var result = LicenseActivationService.Instance.Activate("NTVM-PRM-7Q4X-L2PK");
            Assert.True(result.Success);
            Assert.Equal(LicensingPlanTier.Premium, result.Plan);
            Assert.True(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.True(LicensingUiState.Instance.IsAiAnalysisUnlocked);
            Assert.False(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void Premium_plus_code_unlocks_premium_and_premium_plus()
    {
        using var storageScope = LicenseActivationTestStorage.Begin();
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            var result = LicenseActivationService.Instance.Activate("NTVM-PLUS-7Q4X-L2PK");
            Assert.True(result.Success);
            Assert.Equal(LicensingPlanTier.PremiumPlus, result.Plan);
            Assert.True(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.True(LicensingUiState.Instance.IsAiAnalysisUnlocked);
            Assert.True(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void Persisted_record_roundtrip_verifies_signature_for_premium()
    {
        const string code = "NTVM-PRM-D8QV-5KXM";
        Assert.True(LicenseValidator.TryValidateActivationCode(code, out var plan, out var hash));
        Assert.Equal(LicensingPlanTier.Premium, plan);

        var activatedAt = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        var record = new LicenseRecord
        {
            Plan = LicensingPlanTier.Premium,
            ActivatedAtUtc = activatedAt,
            ActivationHash = hash,
            Signature = LicenseHasher.ComputeSignature(LicensingPlanTier.Premium, activatedAt, hash)
        };

        Assert.True(LicenseValidator.VerifyRecord(record));
    }

    [Fact]
    public void Persisted_record_roundtrip_verifies_signature_for_premium_plus()
    {
        const string code = "NTVM-PLUS-D8QV-5KXM";
        Assert.True(LicenseValidator.TryValidateActivationCode(code, out var plan, out var hash));
        Assert.Equal(LicensingPlanTier.PremiumPlus, plan);

        var activatedAt = new DateTime(2026, 7, 2, 12, 0, 0, DateTimeKind.Utc);
        var record = new LicenseRecord
        {
            Plan = LicensingPlanTier.PremiumPlus,
            ActivatedAtUtc = activatedAt,
            ActivationHash = hash,
            Signature = LicenseHasher.ComputeSignature(LicensingPlanTier.PremiumPlus, activatedAt, hash)
        };

        Assert.True(LicenseValidator.VerifyRecord(record));

        record = new LicenseRecord
        {
            Plan = record.Plan,
            ActivatedAtUtc = record.ActivatedAtUtc,
            ActivationHash = record.ActivationHash,
            Signature = "deadbeef"
        };
        Assert.False(LicenseValidator.VerifyRecord(record));
    }

    [Fact]
    public void Premium_hash_cannot_verify_as_premium_plus_record()
    {
        Assert.True(LicenseValidator.TryValidateActivationCode("NTVM-PRM-7Q4X-L2PK", out _, out var hash));
        var record = new LicenseRecord
        {
            Plan = LicensingPlanTier.PremiumPlus,
            ActivatedAtUtc = DateTime.UtcNow,
            ActivationHash = hash,
            Signature = "00"
        };
        Assert.False(LicenseValidator.VerifyRecord(record));
    }
}

[Collection("LicensingState")]
public sealed class LicensingUiStateGuardTests
{
    [Fact]
    public void Default_state_is_free_without_premium_features()
    {
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            Assert.Equal(LicensingPlanTier.Free, LicensingUiState.Instance.CurrentPlan);
            Assert.False(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.False(LicensingUiState.Instance.IsAiAnalysisUnlocked);
            Assert.False(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void ApplyLicense_ignores_failed_results()
    {
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            LicensingUiState.Instance.ApplyLicense(new LicenseActivationResult
            {
                Success = false,
                Plan = LicensingPlanTier.PremiumPlus,
                Message = LicenseConstants.InvalidActivationMessage
            });
            Assert.False(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.False(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void ApplyLicense_unlocks_premium_only_for_premium_plan()
    {
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            LicensingUiState.Instance.ApplyLicense(new LicenseActivationResult
            {
                Success = true,
                Plan = LicensingPlanTier.Premium,
                Message = LicenseConstants.PremiumActivatedMessage
            });
            Assert.True(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.True(LicensingUiState.Instance.IsAiAnalysisUnlocked);
            Assert.False(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }

    [Fact]
    public void ApplyLicense_unlocks_premium_plus_for_premium_plus_plan()
    {
        LicensingUiState.Instance.ResetLicensedTier();
        try
        {
            LicensingUiState.Instance.ApplyLicense(new LicenseActivationResult
            {
                Success = true,
                Plan = LicensingPlanTier.PremiumPlus,
                Message = LicenseConstants.PremiumPlusActivatedMessage
            });
            Assert.True(LicensingUiState.Instance.IsPremiumUnlocked);
            Assert.True(LicensingUiState.Instance.IsPremiumPlusUnlocked);
        }
        finally
        {
            LicensingUiState.Instance.ResetLicensedTier();
        }
    }
}
