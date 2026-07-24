namespace Netvium.Services.AIGuide.KnowledgePack
{
    public static class TemplateRenderer
    {
        public static string Section(string key, bool useTurkish)
        {
            var sections = KnowledgePackHost.Current.ResponseTemplates.Sections;
            return sections.TryGetValue(key, out var text) ? text.Get(useTurkish) : key;
        }
    }
}
