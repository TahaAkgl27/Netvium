namespace Netvium.Services.AIGuide
{
    internal static class OpenAiEndpointResolver
    {
        public static string Resolve(string? endpoint, string provider, string model)
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
                return endpoint.Trim();

            if (provider.Equals("Azure OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = Environment.GetEnvironmentVariable("NETVIUM_AZURE_OPENAI_ENDPOINT");
                if (!string.IsNullOrWhiteSpace(baseUrl))
                    return $"{baseUrl.TrimEnd('/')}/openai/deployments/{model}/chat/completions?api-version=2024-08-01-preview";
            }

            return "https://api.openai.com/v1/chat/completions";
        }
    }
}
