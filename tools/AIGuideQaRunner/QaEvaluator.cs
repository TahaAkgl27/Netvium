namespace AIGuideQaRunner;

internal static class QaEvaluator
{
    public static (bool Pass, string Reason) Evaluate(QaScenario s, QaActual a)
    {
        var reasons = new List<string>();

        if (s.Category == "Screen Recognition")
        {
            var exp = s.Expected.Result.Replace("Detect ", "").Trim();
            var detected = a.DetectedScreen;
            if (exp == "Unknown")
            {
                if (detected != "Unknown" && !ScreenMatchesConsumerOrTitle(detected, s.Screen))
                    reasons.Add($"Screen: expected Unknown, got {detected}");
            }
            else if (!ScreenMatchesExpected(exp, detected))
            {
                reasons.Add($"Screen: expected {exp}, got {detected}");
            }
        }

        if (s.Expected.Intent != "Unknown" && s.Expected.Intent != "N/A"
            && !a.DetectedIntent.Equals(s.Expected.Intent, StringComparison.OrdinalIgnoreCase)
            && !(s.Expected.Intent == "UserManagement" && a.DetectedIntent == "UserManagement"))
        {
            if (s.Category is "Active Directory" or "DHCP" or "DNS" or "IIS" or "Event Viewer" or "Confidence")
            {
                if (!a.DetectedIntent.Contains(s.Expected.Intent.Split('_')[0], StringComparison.OrdinalIgnoreCase)
                    && a.DetectedIntent != s.Expected.Intent)
                {
                    reasons.Add($"Intent: expected {s.Expected.Intent}, got {a.DetectedIntent}");
                }
            }
            else if (s.Expected.Intent != a.DetectedIntent)
            {
                reasons.Add($"Intent: expected {s.Expected.Intent}, got {a.DetectedIntent}");
            }
        }

        if (s.Expected.ShouldDetectWrongScreen && !a.WrongScreenDetected)
            reasons.Add("Wrong screen not detected");

        if (s.Expected.ShouldRefuse && !a.RefusesWrongTool && s.Category != "Context Memory")
            reasons.Add("Should refuse wrong tool/screen");

        if (s.Category == "Hidden Target" && !a.MentionsHiddenTarget)
            reasons.Add("Should state target not visible");

        if (s.Category == "Hidden Target" && a.UsesWrongSelection)
            reasons.Add("Should not right-click hidden IT while wrong path");

        if (s.Category == "Wrong Object" && a.UsesWrongSelection)
            reasons.Add("Used wrong selected object as target");

        if (s.Category == "Hallucination" && a.HallucinationRisk)
            reasons.Add("Hallucination: references wrong selection as target");

        if (s.Expected.Confidence == "High" && a.Confidence is not "High"
            && s.Category is not "Screen Recognition")
            reasons.Add($"Confidence: expected High, got {a.Confidence}");
        if (s.Expected.Confidence == "Low" && a.Confidence is not "Low"
            && s.Category is not "Screen Recognition")
            reasons.Add($"Confidence: expected Low, got {a.Confidence}");
        if (s.Expected.Confidence == "Medium" && a.Confidence is not "Medium"
            && s.Category is not "Screen Recognition")
            reasons.Add($"Confidence: expected Medium, got {a.Confidence}");

        if (s.Expected.ShouldAskQuestion && !a.AsksQuestion && s.Category == "Clarification")
            reasons.Add("Should ask clarifying question");

        if (s.Category is "Context Memory" or "Conversation")
        {
            var keywords = s.Expected.Result.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Length > 0)
            {
                var haystack = a.GuidanceSnippet;
                if (!keywords.Any(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    reasons.Add($"Missing continuation context (expected one of: {s.Expected.Result})");
            }
        }

        if (s.Category == "Visual Evidence" && !a.HasVisualEvidence)
            reasons.Add("Missing visual evidence in response");

        if (s.Category == "Excel" && !s.UserQuestion.Contains("izin") && a.DetectedIntent != "Unknown")
            reasons.Add("Excel features not supported — should stay Unknown");

        if (s.Category == "Experience")
        {
            if (!a.HasExperienceSection)
                reasons.Add("Missing Dikkat/Attention section");
            var haystack = a.GuidanceSnippet + " " + a.ExperienceHintText;
            var keywords = s.Expected.Result.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (keywords.Length > 0 && !keywords.Any(k => haystack.Contains(k, StringComparison.OrdinalIgnoreCase)))
                reasons.Add($"Missing experience hint (expected one of: {s.Expected.Result})");
        }

        return (reasons.Count == 0, string.Join("; ", reasons));
    }

    private static bool ScreenMatchesExpected(string enumOrKind, string displayName)
    {
        var expectedDisplay = enumOrKind switch
        {
            "FileExplorer" => "File Explorer",
            "ServerManager" => "Server Manager",
            "ActiveDirectoryUsersAndComputers" => "Active Directory Users and Computers",
            "DnsManager" => "DNS Manager",
            "Dhcp" => "DHCP",
            "GroupPolicyManagement" => "Group Policy Management",
            "HyperV" => "Hyper-V Manager",
            "IisManager" => "IIS Manager",
            "EventViewer" => "Event Viewer",
            "TaskScheduler" => "Task Scheduler",
            "RegistryEditor" => "Registry Editor",
            "ComputerManagement" => "Computer Management",
            "Services" => "Services",
            "EndpointSecurity" => "Endpoint Security",
            _ => enumOrKind
        };
        return displayName.Equals(expectedDisplay, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ScreenMatchesConsumerOrTitle(string detected, string scenarioTitle)
    {
        if (scenarioTitle.Contains(detected, StringComparison.OrdinalIgnoreCase))
            return true;

        return detected is "Excel" or "Word" or "Outlook" or "Google Chrome" or "Microsoft Edge"
               or "TeamViewer" or "AnyDesk" or "Paint" or "Notepad";
    }
}
