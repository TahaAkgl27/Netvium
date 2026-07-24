using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    internal static class ObservationGenerator
    {
        public static ObservationResult Generate(VisualMentorInput input)
        {
            var items = new List<ObservationItem>();

            foreach (var (match, build) in ObservationCatalog.Rules)
            {
                try
                {
                    if (match(input))
                        items.Add(build(input));
                }
                catch
                {
                    // Skip failing rules — observation-only engine must stay stable.
                }
            }

            return new ObservationResult
            {
                Items = items
                    .OrderByDescending(i => i.Importance)
                    .ThenByDescending(i => i.Severity)
                    .Take(5)
                    .ToList()
            };
        }
    }
}
