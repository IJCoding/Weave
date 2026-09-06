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
        [SerializeField] private List<ResourceDefinition> resources = new List<ResourceDefinition>();
        [SerializeField] private float secondsPerDistanceUnit = 3f;
        [SerializeField] private float sameLocationPreparationSeconds = 1f;
        [SerializeField] private float carryPenaltyPerWeightUnit = 0.05f;

        private readonly DaySimulationService simulation = new DaySimulationService(new CanonResolver());
        private RunState runState;
        private SimulationSpeedMode selectedSpeedMode = SimulationSpeedMode.Normal;
        private int pauseOverrideDepth;

        public event Action StateChanged;
        public event Action<SimulationAdvanceResult> SimulationAdvanced;

        public RunState RunState => runState;
        public IReadOnlyList<LocationDefinition> Locations => locations;
        public IReadOnlyList<CharacterDefinition> Characters => characters;
        public IReadOnlyList<TaskDefinition> Tasks => tasks;
        public IReadOnlyList<ResourceDefinition> Resources => resources;
        public float SameLocationPreparationSeconds => Mathf.Max(sameLocationPreparationSeconds, 0.01f);
        public float SecondsPerDistanceUnit => Mathf.Max(secondsPerDistanceUnit, 0.01f);
        public float CarryPenaltyPerWeightUnit => Mathf.Max(carryPenaltyPerWeightUnit, 0f);
        public SimulationSpeedMode SelectedSpeedMode => selectedSpeedMode;
        public SimulationSpeedMode EffectiveSpeedMode =>
            pauseOverrideDepth > 0 ? SimulationSpeedMode.Paused : selectedSpeedMode;

        public void Configure(
            GameCalendarDefinition configuredCalendar,
            IEnumerable<LocationDefinition> configuredLocations,
            IEnumerable<CharacterDefinition> configuredCharacters,
            IEnumerable<TaskDefinition> configuredTasks,
            IEnumerable<ResourceDefinition> configuredResources)
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
            resources = configuredResources != null
                ? new List<ResourceDefinition>(configuredResources)
                : new List<ResourceDefinition>();
        }

        public void StartRun(CharacterDefinition controlledCharacter)
        {
            runState = simulation.CreateInitialState(calendarDefinition, locations, characters, controlledCharacter);
            pauseOverrideDepth = 0;
            selectedSpeedMode = SimulationSpeedMode.Normal;
            NotifyStateChanged();
        }

        public float GetEstimatedTravelDuration(TaskDefinition task)
        {
            if (runState == null)
            {
                return 0f;
            }

            return simulation.EstimateTravelDuration(
                runState,
                GetControlledCharacter(),
                task,
                locations,
                resources,
                SecondsPerDistanceUnit,
                SameLocationPreparationSeconds,
                CarryPenaltyPerWeightUnit);
        }

        public float GetCharacterCarriedWeight(string characterId)
        {
            if (runState == null || string.IsNullOrEmpty(characterId))
            {
                return 0f;
            }

            var characterState = runState.GetCharacter(characterId);
            var weightLookup = new Dictionary<string, float>();

            foreach (var resource in resources)
            {
                if (resource == null || string.IsNullOrEmpty(resource.ResourceId))
                {
                    continue;
                }

                weightLookup[resource.ResourceId] = resource.CarryWeightPerUnit;
            }

            var total = 0f;

            foreach (var carried in characterState.CarriedResources)
            {
                if (carried.Value <= 0)
                {
                    continue;
                }

                if (!weightLookup.TryGetValue(carried.Key, out var weightPerUnit))
                {
                    weightPerUnit = 1f;
                }

                total += carried.Value * Mathf.Max(0f, weightPerUnit);
            }

            return total;
        }

        public string GetResourceDisplayName(string resourceId)
        {
            foreach (var resource in resources)
            {
                if (resource != null && resource.ResourceId == resourceId)
                {
                    return string.IsNullOrEmpty(resource.DisplayName) ? resource.ResourceId : resource.DisplayName;
                }
            }

            return resourceId;
        }

        public void SetSimulationSpeed(SimulationSpeedMode speedMode)
        {
            if (selectedSpeedMode == speedMode)
            {
                return;
            }

            selectedSpeedMode = speedMode;
            NotifyStateChanged();
        }

        public void PushPauseOverride()
        {
            pauseOverrideDepth++;
            NotifyStateChanged();
        }

        public void PopPauseOverride()
        {
            if (pauseOverrideDepth <= 0)
            {
                return;
            }

            pauseOverrideDepth--;
            NotifyStateChanged();
        }

        public float GetEffectiveSimulationSpeed()
        {
            switch (EffectiveSpeedMode)
            {
                case SimulationSpeedMode.FastForward:
                    return calendarDefinition != null
                        ? Mathf.Max(calendarDefinition.FastForwardSimulationSpeed, 0f)
                        : 0f;
                case SimulationSpeedMode.Normal:
                    return calendarDefinition != null
                        ? Mathf.Max(calendarDefinition.NormalSimulationSpeed, 0f)
                        : 0f;
                default:
                    return 0f;
            }
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

            var command = simulation.StartTravel(
                runState,
                GetControlledCharacter(),
                task,
                GetEstimatedTravelDuration(task));
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

        public SimulationAdvanceResult AdvanceSimulation(float realSeconds)
        {
            if (runState == null || realSeconds <= 0f)
            {
                return default;
            }

            var simulationSeconds = realSeconds * GetEffectiveSimulationSpeed();

            if (simulationSeconds <= 0f)
            {
                return default;
            }

            var result = simulation.AdvanceSimulation(
                runState,
                calendarDefinition,
                GetControlledCharacter(),
                tasks,
                simulationSeconds);

            if (result.StateChanged)
            {
                NotifyStateChanged();
                SimulationAdvanced?.Invoke(result);
            }

            return result;
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

        private void Update()
        {
            AdvanceSimulation(Time.unscaledDeltaTime);
        }

        private CharacterDefinition GetControlledCharacter()
        {
            if (runState == null)
            {
                return null;
            }

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
