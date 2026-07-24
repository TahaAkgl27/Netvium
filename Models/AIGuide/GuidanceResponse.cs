namespace Netvium.Models.AIGuide
{
    public sealed class GuidanceResponse
    {
        public required string RequestId { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Intent-first answer shown before any steps.</summary>
        public string DirectAnswer { get; set; } = string.Empty;

        /// <summary>Diagnostic/legacy screen label; not required in compact chat output.</summary>
        public string CurrentScreen { get; set; } = string.Empty;

        /// <summary>Short visual summary only; raw OCR must never be placed here.</summary>
        public string WhatICanSee { get; set; } = string.Empty;

        public List<string> ManualSteps { get; init; } = new();
        public string IfYouDoNotSeeIt { get; init; } = string.Empty;
        public string VisibilityNote { get; init; } = string.Empty;
        public string ImportantNote { get; init; } = string.Empty;

        /// <summary>Diagnostics only. The normal chat UI hides this unless capture diagnostics are enabled.</summary>
        public string Confidence { get; set; } = "Medium";
        public ContextConfidence? ConfidenceDetail { get; set; }
        public List<GuidanceEvidence> VisibleEvidence { get; init; } = new();
        public string FormattedAnswer { get; set; } = string.Empty;

        public bool IsCompactVisualAnswer => !string.IsNullOrWhiteSpace(DirectAnswer);

        public static GuidanceResponse NotReady(string requestId) =>
            new()
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = "Complete screen analysis before asking a question."
            };

        public static GuidanceResponse Failed(string requestId, string message) =>
            new()
            {
                RequestId = requestId,
                Success = false,
                ErrorMessage = message
            };
    }
}
