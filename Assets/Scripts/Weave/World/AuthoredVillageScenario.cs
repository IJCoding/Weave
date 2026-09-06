using System.Collections.Generic;
using UnityEngine;
using Weave.Data;

namespace Weave.World
{
    public sealed class AuthoredVillageScenario : MonoBehaviour
    {
        [SerializeField] private GameCalendarDefinition calendarDefinition;
        [SerializeField] private CharacterDefinition controlledCharacter;
        [SerializeField] private List<ResourceDefinition> resources = new List<ResourceDefinition>();
        [SerializeField] private AuthoredVillageWorldRegistry worldRegistry;

        public GameCalendarDefinition CalendarDefinition => calendarDefinition;
        public CharacterDefinition ControlledCharacter => controlledCharacter;
        public IReadOnlyList<ResourceDefinition> Resources => resources;
        public AuthoredVillageWorldRegistry WorldRegistry => worldRegistry;

        private void OnValidate()
        {
            if (worldRegistry == null)
            {
                worldRegistry = GetComponentInChildren<AuthoredVillageWorldRegistry>();
            }
        }
    }
}
