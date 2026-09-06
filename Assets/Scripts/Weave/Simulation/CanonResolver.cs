using Weave.Data;
using Weave.Runtime;

namespace Weave.Simulation
{
    public sealed class CanonResolver
    {
        public string ResolveOption(CharacterDefinition character, string decisionKey, PlayerCanonState playerCanon)
        {
            if (character == null || string.IsNullOrWhiteSpace(decisionKey))
            {
                return string.Empty;
            }

            if (playerCanon != null &&
                playerCanon.TryGetOption(character.CharacterId, decisionKey, out var playerOption))
            {
                return playerOption;
            }

            foreach (var fallback in character.DeveloperCanon)
            {
                if (fallback.DecisionKey == decisionKey)
                {
                    return fallback.DefaultOptionId;
                }
            }

            return string.Empty;
        }
    }
}
