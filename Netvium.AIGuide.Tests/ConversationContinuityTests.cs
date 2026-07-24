using Netvium.Models.AIGuide;
using Netvium.Models.AIGuide.Decision;
using Netvium.Services.AIGuide.Decision;
using Xunit;

namespace Netvium.AIGuide.Tests;

public sealed class ConversationContinuityTests
{
    private static readonly IWorkflowDecisionEngine Engine = AIGuideCompositionRoot.CreateDecisionEngine();
    private static readonly WorkflowResponseGenerator Responses = new();

    [Fact]
    public void FolderPermission_ThreeTurnFlow_ReachesSecurityTab()
    {
        var anchor = "D:\\Shared\\IT klasörüne Taha'ya Read izni vereceğim";
        var explorerNoTarget = Visual(MmcConsoleKind.FileExplorer, "", "D:\\Shared");

        var turn1 = Decide(anchor, explorerNoTarget, null, []);
        Assert.Equal(AdminUserIntent.FolderPermission, turn1.Context.Intent);
        Assert.Contains("navigate", turn1.NextStep.StepId, StringComparison.OrdinalIgnoreCase);

        var session1 = WorkflowSessionState.FromDecision(turn1, anchor);
        var explorerWithIt = Visual(MmcConsoleKind.FileExplorer, "IT", "D:\\Shared\\IT");
        var prior = new[] { UserTurn(anchor) };

        var turn2 = Decide("Tamam klasörü açtım.", explorerWithIt, session1, prior);
        Assert.Equal(AdminUserIntent.FolderPermission, turn2.Context.Intent);
        Assert.Equal(WorkflowScreenState.TargetVisible, turn2.Context.CurrentState);
        Assert.Contains("properties", turn2.NextStep.Instruction, StringComparison.OrdinalIgnoreCase);

        var session2 = WorkflowSessionState.FromDecision(turn2, anchor);
        var prior2 = new[] { UserTurn(anchor), UserTurn("Tamam klasörü açtım.") };

        var turn3 = Decide("Devam.", explorerWithIt, session2, prior2);
        Assert.Equal(AdminUserIntent.FolderPermission, turn3.Context.Intent);
        Assert.Contains("security", turn3.NextStep.Instruction, StringComparison.OrdinalIgnoreCase);

        var response = Responses.Generate(turn3, "test-continuity");
        Assert.Contains("Security", response.FormattedAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextMemory_SimdiNeYapacagim_KeepsItFolderGoal()
    {
        var anchor = "D:\\Shared\\IT klasörüne Taha'ya Read izni vereceğim";
        var visual = Visual(MmcConsoleKind.FileExplorer, "IT", "D:\\Shared\\IT");
        var session = WorkflowSessionState.FromDecision(
            Decide(anchor, visual, null, []),
            anchor);

        var decision = Decide(
            "şimdi ne yapacağım",
            visual,
            session,
            [UserTurn(anchor)]);

        Assert.Equal(AdminUserIntent.FolderPermission, decision.Context.Intent);
        Assert.Equal("Taha", decision.Context.Target.UserName);
        Assert.Contains("IT", decision.Context.Target.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static WorkflowDecisionResult Decide(
        string question,
        ScreenVisualContext visual,
        WorkflowSessionState? session,
        IReadOnlyList<ConversationTurn> priorTurns) =>
        Engine.Decide(new WorkflowDecisionInput
        {
            UserQuestion = question,
            VisualContext = visual,
            PriorTurns = priorTurns,
            SessionState = session
        });

    private static ConversationTurn UserTurn(string content) =>
        new() { Role = "user", Content = content };

    private static ScreenVisualContext Visual(MmcConsoleKind kind, string selected, string path) =>
        new()
        {
            ActiveWindowTitle = $"File Explorer {path}",
            OcrWindowTitle = "File Explorer",
            MmcConsole = kind,
            SelectedListItem = selected,
            OcrSelectedRow = selected,
            OcrStatusBar = path,
            StatusBarText = path,
            OcrConfidence = 0.9
        };
}
