using System.Text.Json;
using Netvium.Models.AIGuide;
using Netvium.Services.AIGuide;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class VisualGuidanceContractTests
{
    [Fact]
    public void NewQueryQuestion_UsesAttachedImageAndProducesIntentFirstCompactAnswer()
    {
        var request = CreateRequest("Yeni sorgu nerden yazcam?", includeNoise: true);
        var payload = ConfigurableGuidanceProvider.BuildRequestPayloadJson(request, Options());
        using var document = JsonDocument.Parse(payload);
        var userContent = document.RootElement.GetProperty("messages")[1].GetProperty("content");
        Assert.Equal("image_url", userContent[1].GetProperty("type").GetString());
        Assert.Equal("high", userContent[1].GetProperty("image_url").GetProperty("detail").GetString());
        var prompt = userContent[0].GetProperty("text").GetString()!;
        Assert.True(prompt.IndexOf("UserQuestion:", StringComparison.Ordinal) < prompt.IndexOf("OCRText", StringComparison.Ordinal));
        Assert.Contains("LocateControl", prompt);

        var parsed = GuidanceResponseParser.TryParse("q1", """
            {"directAnswer":"SQL yönetim ekranında üst araç çubuğundaki New Query / Yeni Sorgu düğmesini kullanın.",
             "steps":["Üst araç çubuğunda New Query düğmesini seçin."],
             "alternative":"Object Explorer'da ilgili veritabanına sağ tıklayıp New Query seçeneğini açın.",
             "visibilityNote":"Üst araç çubuğu görüntüde kısmen kırpılmış.","confidence":"Medium","visibleEvidence":[]}
            """);
        Assert.NotNull(parsed);
        Assert.Contains("New Query", parsed!.FormattedAnswer);
        Assert.DoesNotContain("Geçerli ekran", parsed.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorQuestion_PrioritizesVisibleErrorExplanationAndSafeCheck()
    {
        var request = CreateRequest("Bu hata ne?");
        request.Observation.VisibleErrors.Add(new VisibleError
        {
            Message = "Login failed", Location = "center dialog", Confidence = 0.95
        });
        var prompt = ConfigurableGuidanceProvider.BuildUserPrompt(request, JsonSerializer.Serialize(request.Observation));
        Assert.Equal(VisualQuestionIntent.ExplainError, VisualQuestionIntentClassifier.Classify(request.UserQuestion));
        Assert.Contains("Explain the visible error message first", prompt);
        Assert.Contains("one safe verification step", prompt);
    }

    [Fact]
    public void ConfirmationQuestion_DoesNotPermitInventingUnreadableButtons()
    {
        var request = CreateRequest("Neye basacağım?");
        var prompt = ConfigurableGuidanceProvider.BuildUserPrompt(request, "{}");
        Assert.Equal(VisualQuestionIntent.ChooseVisibleAction, VisualQuestionIntentClassifier.Classify(request.UserQuestion));
        Assert.Contains("only when button labels are readable", prompt);
        Assert.Contains("do not invent labels", prompt);
    }

    [Fact]
    public void CroppedToolbar_FormatsOneStandardAlternativeAndVisibilityNote()
    {
        var parsed = GuidanceResponseParser.TryParse("q4", """
            {"directAnswer":"Gerekli düğme bu görüntüde görünmüyor.","steps":[],
             "alternative":"Uygulamanın standart menü yolunu kullanın.",
             "visibilityNote":"Üst araç çubuğu ekranın dışında kalmış.","confidence":"Low","visibleEvidence":[]}
            """);
        Assert.NotNull(parsed);
        Assert.Contains("standart menü yolunu", parsed!.FormattedAnswer);
        Assert.Contains("araç çubuğu ekranın dışında", parsed.FormattedAnswer);
        Assert.DoesNotContain("Güven:", parsed.FormattedAnswer);
    }

    [Fact]
    public void NoisySqlOcr_IsRemovedFromUserFacingAnswerAndEvidence()
    {
        var guarded = VisualGuidanceResponseGuard.Apply(new GuidanceResponse
        {
            RequestId = "q5",
            Success = true,
            DirectAnswer = "Yeni işlem için uygulamanın araç çubuğunu kullanın.",
            WhatICanSee = "dbo.tb_KitapTalep dbo.tb_KitapTur dbo.tb_Kullanici NULL NULL 53",
            ManualSteps = ["dbo.tb_A dbo.tb_B NULL NULL 44"],
            VisibleEvidence =
            [
                new GuidanceEvidence { Label = "OCR", Detail = "dbo.tb_A dbo.tb_B 2448SYHOLH48 NULL NULL" }
            ]
        });
        Assert.DoesNotContain("dbo.", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(guarded.VisibleEvidence);
        Assert.Empty(guarded.ManualSteps);
    }

    [Fact]
    public void ObserveOnly_OutputCannotClaimActionsWerePerformed()
    {
        var guarded = VisualGuidanceResponseGuard.Apply(new GuidanceResponse
        {
            RequestId = "q6",
            Success = true,
            DirectAnswer = "Düğmeye tıkladım ve pencereyi açtım.",
            ManualSteps = ["Komutu çalıştırdım."]
        });
        Assert.DoesNotContain("tıkladım", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("açtım", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("çalıştırdım", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tıklayın", guarded.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SensitiveScreenValues_AreRedactedBeforeDisplayOrPromptSupport()
    {
        const string sensitive = "password=Secret123 token=abc123 Server=db01;Database=Prod;User Id=sa;Password=P@ss; user@example.com 550e8400-e29b-41d4-a716-446655440000 ABCDE-FGHIJ-KLMNO-PQRST-UVWXY";
        var redacted = AIGuideSecretRedactor.Redact(sensitive);
        Assert.DoesNotContain("Secret123", redacted);
        Assert.DoesNotContain("abc123", redacted);
        Assert.DoesNotContain("db01", redacted);
        Assert.DoesNotContain("user@example.com", redacted);
        Assert.DoesNotContain("550e8400", redacted);
        Assert.DoesNotContain("ABCDE-FGHIJ", redacted);
    }

    [Fact]
    public void InformalTurkishSpelling_NormalizesToLocateControlIntentWithoutAppHardCode()
    {
        Assert.Equal(VisualQuestionIntent.LocateControl,
            VisualQuestionIntentClassifier.Classify("yeni bişey nerden yazcam"));
        Assert.DoesNotContain("SQL Server", VisualQuestionIntentClassifier.PromptDirective(VisualQuestionIntent.LocateControl));
        Assert.Contains("control label", VisualQuestionIntentClassifier.PromptDirective(VisualQuestionIntent.LocateControl));
    }

    private static GuidanceRequest CreateRequest(string question, bool includeNoise = false)
    {
        var observation = new VisionObservation
        {
            Application = "SQL management application",
            WindowTitle = "Database management window",
            ScreenType = "database-grid",
            Confidence = 0.75
        };
        if (includeNoise)
        {
            observation.VisibleText.Add(new VisibleTextBlock
            {
                Text = "dbo.tb_KitapTalep dbo.tb_KitapTur dbo.tb_Kullanici 2448SYHOLH48 NULL 53",
                Location = "data grid",
                Confidence = 0.8
            });
        }

        return new GuidanceRequest
        {
            RequestId = Guid.NewGuid().ToString("N"),
            UserQuestion = question,
            Observation = observation,
            ObservationJson = JsonSerializer.Serialize(observation),
            CaptureSource = ScreenCaptureSource.RdpSession,
            AnalyzedAtUtc = DateTime.UtcNow,
            ScreenshotImageBytes = [1, 2, 3, 4],
            ScreenshotWidth = 1920,
            ScreenshotHeight = 1080,
            GuideMode = "ObserveOnly",
            IsFreshVisualContext = true
        };
    }

    private static GuidanceProviderOptions Options() => new()
    {
        Provider = "OpenAI",
        ApiKey = "test-only",
        Model = "gpt-4o-mini",
        TimeoutSeconds = 10
    };
}
