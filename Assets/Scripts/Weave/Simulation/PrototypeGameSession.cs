using System;
using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;

namespace Weave.Simulation
{
    public sealed class PrototypeGameSession : MonoBehaviour
    {
        [SerializeField] private GameCalendarDefinition calendarDefinition;
        [SerializeField] private List<LocationDefinition> locations = new List<LocationDefinition>();
        [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();
        [SerializeField] private List<TaskDefinition> tasks = new List<TaskDefinition>();

        private readonly DaySimulationService simulation = new DaySimulationService(new CanonResolver());
        private RunState runState;

        public event Action StateChanged;

        public RunState RunState => runState;
        public IReadOnlyList<LocationDefinition> Locations => locations;
        public IReadOnlyList<CharacterDefinition> Characters => characters;

        public void Configure(
            GameCalendarDefinition configuredCalendar,
            IEnumerable<LocationDefinition> configuredLocations,
            IEnumerable<CharacterDefinition> configuredCharacters,
            IEnumerable<TaskDefinition> configuredTasks)
        {
            calendarDefinition = configuredCalendar;
            locations = configuredLocations != null
                ? new List<LocationDefinition>(configuredLocations)
                : new List<LocationDefinition>();
            characters = configuredCharacters != null
                ? new List<CharacterDefinition>(configuredCharacters)
                : new List<CharacterDefinition>();
            tasks = configuredTasks != null
                ? new List<TaskDefinition>(configuredTasks)
                : new List<TaskDefinition>();
        }

        public void StartRun(CharacterDefinition controlledCharacter)
        {
            runState = simulation.CreateInitialState(calendarDefinition, locations, characters, controlledCharacter);
            NotifyStateChanged();
        }

        public List<TaskDefinition> GetPlayerTasks()
        {
            if (runState == null)
            {
                return new List<TaskDefinition>();
            }

            return simulation.GetAvailableTasks(GetControlledCharacter(), tasks, runState);
        }

        public TravelCommand AssignPlayerTask(TaskDefinition task)
        {
            if (runState == null)
            {
                return default;
            }

            var command = simulation.StartTravel(runState, GetControlledCharacter(), task);
            NotifyStateChanged();
            return command;
        }

        public void TickCharacterTravel(string characterId, float travelStep)
        {
            if (runState == null)
            {
                return;
            }

            if (simulation.TickTravel(runState, characterId, travelStep))
            {
                NotifyStateChanged();
            }
        }

        public void ResolvePlayerTask(TaskDefinition task)
        {
            if (runState == null)
            {
                return;
            }

            if (simulation.ResolveTask(runState, GetControlledCharacter(), task))
            {
                NotifyStateChanged();
            }
        }

        public EventResolution ResolvePlayerEvent(EventDefinition eventDefinition, string selectedOptionId)
        {
            if (runState == null)
            {
                return default;
            }

            var resolution = simulation.ResolveEvent(runState, eventDefinition, selectedOptionId);
            NotifyStateChanged();
            return resolution;
        }

        public EventResolution ResolveNpcEvent(PlayerCanonState playerCanon, EventDefinition eventDefinition)
        {
            if (runState == null)
            {
                return default;
            }

            var resolution = simulation.ResolveNpcEvent(runState, playerCanon, eventDefinition);
            NotifyStateChanged();
            return resolution;
        }

        public void AdvanceDay()
        {
            if (runState == null)
            {
                return;
            }

            simulation.AdvanceDay(runState, calendarDefinition);
            NotifyStateChanged();
        }

        public Vector2 GetCharacterMapPosition(string characterId)
        {
            if (runState == null)
            {
                return Vector2.zero;
            }

            var characterState = runState.GetCharacter(characterId);
            var origin = GetLocationPosition(characterState.TravelOriginLocationId);
            var destination = GetLocationPosition(characterState.TravelDestinationLocationId);

            if (!characterState.IsTravelling)
            {
                return GetLocationPosition(characterState.CurrentLocationId);
            }

            return Vector2.Lerp(origin, destination, characterState.TravelProgress);
        }

        public Color GetCharacterColor(string characterId)
        {
            foreach (var character in characters)
            {
                if (character.CharacterId == characterId)
                {
                    return character.MapColor;
                }
            }

            return Color.white;
        }

        private CharacterDefinition GetControlledCharacter()
        {
            foreach (var character in characters)
            {
                if (character.CharacterId == runState.ControlledCharacterId)
                {
                    return character;
                }
            }

            return null;
        }

        private Vector2 GetLocationPosition(string locationId)
        {
            foreach (var location in locations)
            {
                if (location.LocationId == locationId)
                {
                    return location.MapPosition;
                }
            }

            return Vector2.zero;
        }

        private void NotifyStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
