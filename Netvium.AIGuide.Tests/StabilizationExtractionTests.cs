using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class StabilizationExtractionTests
{
    private static readonly TargetExtractionService Extractor = new();

    public StabilizationExtractionTests() => AIGuideTestHost.EnsureLoaded();

    [Fact]
    public void InlineRegex_MatchesTurkishFolderPhrase()
    {
        var q = "IT klasörüne izin vereceğim";
        var match = System.Text.RegularExpressions.Regex.Match(q,
            @"([A-Za-zÇçĞğİıÖöŞşÜü0-9._-]+)\s*klasör\w*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"regex failed on '{q}'");
        Assert.Equal("IT", match.Groups[1].Value);

        match = System.Text.RegularExpressions.Regex.Match("IT izin Taha",
            @"^([A-Za-zÇçĞğİıÖöŞşÜü0-9._-]+)\s+izin",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
            | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        Assert.Equal("IT", match.Groups[1].Value);
    }

    [Fact]
    public void ExtractFolderHint_IgnoresDemonstratives()
    {
        Assert.Equal(string.Empty, FolderPermissionExtractor.ExtractFolderHint("Bu klasöre Taha read"));
    }

    [Fact]
    public void ExtractFolderHint_MatchesUnicodeSafePatterns()
    {
        Assert.Equal("IT", FolderPermissionExtractor.ExtractFolderHint("IT klasörüne izin vereceğim"));
        Assert.Equal("IT", FolderPermissionExtractor.ExtractFolderHint("IT izin Taha"));
        Assert.Equal("IT", FolderPermissionExtractor.ExtractFolderHint("Taha'ya IT'de read access"));
    }

    [Fact]
    public void ExtractGoal_ParsesFolderHints()
    {
        var goal = FolderPermissionExtractor.ExtractGoal("IT klasörüne izin vereceğim", useTurkish: true);
        Assert.Equal("IT", goal.TargetFolderName);
    }

    [Theory]
    [InlineData("IT klasörüne izin vereceğim", "IT")]
    [InlineData("IT izin Taha", "IT")]
    [InlineData("Taha'ya IT'de read access", "IT")]
    public void ExtractFolderPermission_ParsesFolderFromQuestion(string question, string expectedFolder)
    {
        var target = Extractor.Extract(
            AdminUserIntent.FolderPermission,
            question,
            useTurkish: true,
            visualContext: null);

        Assert.Equal(expectedFolder, target.FolderName);
    }

    [Fact]
    public void IsTargetVisible_FalseWhenFileSelectedButFolderMentionedInQuestion()
    {
        var target = new WorkflowTarget { FolderName = "IT" };
        var ctx = new ScreenVisualContext
        {
            MmcConsole = MmcConsoleKind.FileExplorer,
            SelectedListItem = "backup.zip",
            ActiveWindowTitle = "File Explorer D:\\Shared",
            OcrStatusBar = "D:\\Shared"
        };

        Assert.False(WorkflowGuideSupport.IsTargetVisibleOnScreen(target, ctx));
    }

    [Fact]
    public void ExtractFolderPermission_PrefersQuestionFolderOverSelection()
    {
        var target = Extractor.Extract(
            AdminUserIntent.FolderPermission,
            "Taha'ya IT'de read access",
            useTurkish: false,
            visualContext: new ScreenVisualContext
            {
                SelectedListItem = "Finance",
                OcrStatusBar = "D:\\Shared",
                StatusBarText = "D:\\Shared",
                MmcConsole = MmcConsoleKind.FileExplorer
            });

        Assert.Equal("IT", target.FolderName);
        Assert.False(target.Path.Contains("Finance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExtractTargetUser_ParsesTrailingNameAfterPath()
    {
        var user = FolderPermissionExtractor.ExtractTargetUserPublic(
            "Paylaşım izni de kontrol et D:\\Shared\\IT Taha");
        Assert.Equal("Taha", user);
    }
}
