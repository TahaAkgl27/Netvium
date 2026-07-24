using Netvium.Models.AIGuide;

namespace Netvium.Services.AIGuide
{
    public sealed class ObservationEngine
    {
        public ObservationResult Generate(VisualMentorInput input) =>
            ObservationGenerator.Generate(input);
    }
}
