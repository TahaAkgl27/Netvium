using System.Text;
using System.Text.Json;

namespace AIGuideQaRunner;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            var datasetPath = QaScenarioLoader.ResolveDatasetPath();
            var scenarios = QaScenarioLoader.Load(datasetPath);
            return RunSuite(scenarios, datasetPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QA Runner failed: {ex.Message}");
            return 2;
        }
    }

    private static int RunSuite(IReadOnlyList<QaScenario> scenarios, string datasetPath)
    {
        var results = new List<object>();
        var passByCategory = new Dictionary<string, (int Pass, int Total)>();
        var dimensionScores = new Dictionary<string, List<bool>>
        {
            ["ScreenDetection"] = [],
            ["WorkflowAccuracy"] = [],
            ["IntentAccuracy"] = [],
            ["Hallucination"] = [],
            ["VisualUnderstanding"] = [],
            ["WrongScreenRecovery"] = [],
            ["ConversationMemory"] = [],
            ["GuidanceQuality"] = [],
            ["ConfidenceAccuracy"] = [],
        };

        Console.WriteLine($"AI Guide QA Runner — {scenarios.Count} scenarios");
        Console.WriteLine($"Dataset: {datasetPath}\n");

        foreach (var s in scenarios)
        {
            var actual = QaEngine.Evaluate(s.Context, s.UserQuestion);
            var (pass, reason) = QaEvaluator.Evaluate(s, actual);

            if (!passByCategory.ContainsKey(s.Category))
                passByCategory[s.Category] = (0, 0);
            var cat = passByCategory[s.Category];
            passByCategory[s.Category] = (cat.Pass + (pass ? 1 : 0), cat.Total + 1);

            TrackDimension(s, actual, pass, dimensionScores);

            results.Add(new
            {
                s.TestNo,
                s.Category,
                s.Screen,
                s.UserQuestion,
                ExpectedIntent = s.Expected.Intent,
                ExpectedWorkflow = s.Expected.Workflow,
                ExpectedConfidence = s.Expected.Confidence,
                ShouldAskQuestion = s.Expected.ShouldAskQuestion,
                ShouldRefuse = s.Expected.ShouldRefuse,
                ShouldContinue = s.Expected.ShouldContinue,
                ShouldDetectWrongScreen = s.Expected.ShouldDetectWrongScreen,
                ExpectedResult = s.Expected.Result,
                ActualResult = actual.GuidanceSnippet,
                DetectedScreen = actual.DetectedScreen,
                DetectedIntent = actual.DetectedIntent,
                ActualConfidence = actual.Confidence,
                Pass = pass ? "PASS" : "FAIL",
                FailureReason = reason,
                ImprovementSuggestion = pass ? "" : Suggest(s, actual, reason)
            });

            Console.WriteLine($"#{s.TestNo:D3} [{s.Category}] {(pass ? "PASS" : "FAIL")} — {s.Screen}");
            if (!pass)
                Console.WriteLine($"     {reason}");
        }

        var totalPass = results.Count(r => ((dynamic)r).Pass == "PASS");
        var pct = (double)totalPass / scenarios.Count * 100;

        var scores = new Dictionary<string, double>
        {
            ["Screen Detection %"] = Pct(dimensionScores["ScreenDetection"]),
            ["Workflow Accuracy %"] = Pct(dimensionScores["WorkflowAccuracy"]),
            ["Intent Accuracy %"] = Pct(dimensionScores["IntentAccuracy"]),
            ["Hallucination Prevention %"] = Pct(dimensionScores["Hallucination"]),
            ["Visual Understanding %"] = Pct(dimensionScores["VisualUnderstanding"]),
            ["Wrong Screen Recovery %"] = Pct(dimensionScores["WrongScreenRecovery"]),
            ["Conversation Memory %"] = Pct(dimensionScores["ConversationMemory"]),
            ["Guidance Quality %"] = Pct(dimensionScores["GuidanceQuality"]),
            ["Confidence Accuracy %"] = Pct(dimensionScores["ConfidenceAccuracy"]),
        };
        scores["Overall Production Readiness %"] = scores.Values.Average();

        var outDir = Path.Combine(AppContext.BaseDirectory, "qa-output");
        Directory.CreateDirectory(outDir);
        var jsonPath = Path.Combine(outDir, "ai-guide-qa-results.json");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(new { totalPass, total = scenarios.Count, scores, passByCategory, results },
            new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);

        Console.WriteLine($"\n=== SUMMARY ===");
        Console.WriteLine($"PASS: {totalPass}/{scenarios.Count} ({pct:F1}%)");
        foreach (var (k, v) in scores.OrderByDescending(x => x.Key == "Overall Production Readiness %" ? 1 : 0))
            Console.WriteLine($"{k}: {v:F1}");
        Console.WriteLine($"\nFull results: {jsonPath}");

        return totalPass == scenarios.Count ? 0 : 1;
    }

    private static void TrackDimension(QaScenario s, QaActual a, bool pass, Dictionary<string, List<bool>> d)
    {
        if (s.Category == "Screen Recognition")
            d["ScreenDetection"].Add(a.DetectedScreen != "Unknown" || s.Expected.Result.Contains("Unknown"));

        if (s.Category is "Workflow" or "Visible Target" or "Server Manager" or "Active Directory" or "DHCP" or "DNS" or "IIS")
            d["WorkflowAccuracy"].Add(pass);

        if (s.Expected.Intent != "Unknown" && s.Expected.Intent != "N/A")
            d["IntentAccuracy"].Add(a.DetectedIntent == s.Expected.Intent || pass);

        if (s.Category == "Hallucination" || s.Category == "Wrong Object")
            d["Hallucination"].Add(!a.HallucinationRisk && pass);

        if (s.Category == "Visual Evidence")
            d["VisualUnderstanding"].Add(a.HasVisualEvidence);

        if (s.Category is "Wrong Screen" or "Recovery")
            d["WrongScreenRecovery"].Add(a.WrongScreenDetected || a.RefusesWrongTool);

        if (s.Category is "Context Memory" or "Conversation")
            d["ConversationMemory"].Add(pass);

        if (s.Category is "Visible Target" or "Hidden Target" or "Workflow" or "FolderPermission")
            d["GuidanceQuality"].Add(pass);

        if (s.Expected.Confidence is "High" or "Medium" or "Low")
            d["ConfidenceAccuracy"].Add(string.Equals(a.Confidence, s.Expected.Confidence, StringComparison.OrdinalIgnoreCase));
    }

    private static double Pct(List<bool> items) =>
        items.Count == 0 ? 0 : items.Count(x => x) * 100.0 / items.Count;

    private static string Suggest(QaScenario s, QaActual a, string reason)
    {
        if (reason.Contains("Screen"))
            return $"Add OCR pattern for '{s.Screen}' in MmcConsoleDetector";
        if (reason.Contains("Intent"))
            return $"Extend AdminToolExpertEngine.DetectIntent for: {s.UserQuestion}";
        if (reason.Contains("Wrong screen"))
            return "Strengthen unknown-screen guard and explicit 'wrong screen' message";
        if (reason.Contains("not visible"))
            return "FolderPermissionGuidanceEngine: enforce navigate-before-right-click";
        if (reason.Contains("continuation context"))
            return "Verify conversation continuity and prior-turn session in decision engine";
        if (reason.Contains("Visual evidence"))
            return "Add structured VisibleEvidence block to FolderPermissionGuidanceLanguage";
        if (reason.Contains("Confidence"))
            return "Tune ComputeConfidence thresholds";
        return "Review prompt directive and local guidance builder";
    }
}
