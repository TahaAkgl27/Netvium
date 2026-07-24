using System.Text.Json;
using Netvium.Models.AIGuide;

namespace AIGuideQaRunner;

internal static class QaScenarioLoader
{
    private static readonly HashSet<string> AllowedConfidence =
        new(StringComparer.OrdinalIgnoreCase) { "High", "Medium", "Low", "N/A" };

    public static IReadOnlyList<QaScenario> Load(string datasetPath)
    {
        if (!File.Exists(datasetPath))
            throw new FileNotFoundException($"QA dataset not found: {datasetPath}");

        var json = File.ReadAllText(datasetPath);
        var file = JsonSerializer.Deserialize<QaScenarioFile>(json, JsonOptions)
            ?? throw new InvalidOperationException("QA dataset deserialized to null.");

        if (file.Scenarios.Count == 0)
            throw new InvalidOperationException("QA dataset contains no scenarios.");

        ValidateDataset(file);

        return file.Scenarios
            .OrderBy(s => s.TestNo)
            .Select(MapScenario)
            .ToList();
    }

    public static string ResolveDatasetPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "TestData", "qa-scenarios-full.json"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "qa-scenarios-full.json")
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        throw new FileNotFoundException(
            "qa-scenarios-full.json not found. Expected under TestData next to the runner output.");
    }

    private static QaScenario MapScenario(QaScenarioDto dto) =>
        new()
        {
            TestNo = dto.TestNo,
            Category = dto.Category,
            Screen = dto.Screen,
            UserQuestion = dto.UserQuestion,
            Expected = new QaExpected
            {
                Intent = dto.ExpectedIntent,
                Workflow = dto.ExpectedWorkflow,
                Confidence = dto.ExpectedConfidence,
                ShouldAskQuestion = dto.ShouldAskQuestion,
                ShouldRefuse = dto.ShouldRefuse,
                ShouldContinue = dto.ShouldContinue,
                ShouldDetectWrongScreen = dto.ShouldDetectWrongScreen,
                Result = dto.ExpectedResult
            },
            Context = MapContext(dto.Context, dto.TestNo)
        };

    private static QaContext MapContext(QaContextDto dto, int testNo)
    {
        if (!Enum.TryParse<MmcConsoleKind>(dto.ScreenKind, ignoreCase: true, out var kind)
            || !Enum.IsDefined(typeof(MmcConsoleKind), kind))
        {
            throw new InvalidOperationException(
                $"Test #{testNo}: invalid context.screenKind '{dto.ScreenKind}'. " +
                $"Must be a valid {nameof(MmcConsoleKind)} enum value.");
        }

        return new QaContext
        {
            ScreenText = dto.ScreenText,
            ScreenKind = kind,
            SelectedListItem = dto.SelectedListItem,
            SelectedTreeNode = dto.SelectedTreeNode,
            AddressOrPathVisible = dto.AddressOrPathVisible,
            PriorUserMessage = dto.PriorUserMessage,
            PriorTurns = dto.PriorTurns
        };
    }

    private static void ValidateDataset(QaScenarioFile file)
    {
        var errors = new List<string>();
        var seen = new HashSet<int>();

        foreach (var s in file.Scenarios)
        {
            if (s.TestNo <= 0)
                errors.Add($"Test #{s.TestNo}: testNo must be positive.");

            if (!seen.Add(s.TestNo))
                errors.Add($"Duplicate testNo: {s.TestNo}.");

            if (string.IsNullOrWhiteSpace(s.Category))
                errors.Add($"Test #{s.TestNo}: category is required.");

            if (string.IsNullOrWhiteSpace(s.UserQuestion))
                errors.Add($"Test #{s.TestNo}: userQuestion is required.");

            if (string.IsNullOrWhiteSpace(s.ExpectedConfidence)
                || !AllowedConfidence.Contains(s.ExpectedConfidence))
            {
                errors.Add(
                    $"Test #{s.TestNo}: expectedConfidence must be one of: {string.Join(", ", AllowedConfidence)}.");
            }

            if (string.IsNullOrWhiteSpace(s.Context?.ScreenKind))
                errors.Add($"Test #{s.TestNo}: context.screenKind is required.");
        }

        if (file.ScenarioCount > 0 && file.ScenarioCount != file.Scenarios.Count)
        {
            errors.Add(
                $"scenarioCount ({file.ScenarioCount}) does not match scenarios array length ({file.Scenarios.Count}).");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("QA dataset validation failed:\n- " + string.Join("\n- ", errors));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
