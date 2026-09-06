using System;
using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;

namespace Weave.Simulation
{
    public enum ActionPhaseType
    {
        TravelPreparation,
        Work,
        ReturnTravel,
        Deposit
    }

    public readonly struct ActionPhaseProgress
    {
        public ActionPhaseProgress(
            ActionPhaseType phaseType,
            string label,
            float durationSeconds,
            float elapsedSeconds)
        {
            PhaseType = phaseType;
            Label = label;
            DurationSeconds = Mathf.Max(durationSeconds, 0f);
            ElapsedSeconds = Mathf.Clamp(elapsedSeconds, 0f, DurationSeconds);
        }

        public ActionPhaseType PhaseType { get; }
        public string Label { get; }
        public float DurationSeconds { get; }
        public float ElapsedSeconds { get; }
        public float Progress => DurationSeconds <= Mathf.Epsilon ? 1f : Mathf.Clamp01(ElapsedSeconds / DurationSeconds);
    }

    public readonly struct ActionProgressSummary
    {
        public ActionProgressSummary(
            string taskId,
            bool isActive,
            int currentPhaseIndex,
            IReadOnlyList<ActionPhaseProgress> phases)
        {
            TaskId = taskId;
            IsActive = isActive;
            CurrentPhaseIndex = currentPhaseIndex;
            Phases = phases ?? new List<ActionPhaseProgress>();
            var totalDuration = 0f;
            var completedDuration = 0f;

            foreach (var phase in Phases)
            {
                totalDuration += Mathf.Max(phase.DurationSeconds, 0f);
                completedDuration += Mathf.Clamp(phase.ElapsedSeconds, 0f, phase.DurationSeconds);
            }

            TotalDurationSeconds = totalDuration;
            CompletedDurationSeconds = completedDuration;
            CurrentPhase = currentPhaseIndex >= 0 && currentPhaseIndex < Phases.Count
                ? Phases[currentPhaseIndex]
                : default;
        }

        public string TaskId { get; }
        public bool IsActive { get; }
        public int CurrentPhaseIndex { get; }
        public IReadOnlyList<ActionPhaseProgress> Phases { get; }
        public float TotalDurationSeconds { get; }
        public float CompletedDurationSeconds { get; }
        public ActionPhaseProgress CurrentPhase { get; }
        public bool HasPhases => Phases != null && Phases.Count > 0;
        public float OverallProgress =>
            TotalDurationSeconds <= Mathf.Epsilon ? 0f : Mathf.Clamp01(CompletedDurationSeconds / TotalDurationSeconds);
        public float CurrentPhaseProgress => CurrentPhase.DurationSeconds <= Mathf.Epsilon
            ? 0f
            : Mathf.Clamp01(CurrentPhase.ElapsedSeconds / CurrentPhase.DurationSeconds);
        public float CurrentPhaseRemainingSeconds =>
            Mathf.Max(0f, CurrentPhase.DurationSeconds - CurrentPhase.ElapsedSeconds);
    }

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

        public ActionProgressSummary GetActionProgressForCharacter(string characterId)
        {
            if (runState == null || string.IsNullOrEmpty(characterId))
            {
                return new ActionProgressSummary(string.Empty, false, -1, new List<ActionPhaseProgress>());
            }

            var characterState = runState.GetCharacter(characterId);

            if (!characterState.HasActiveTask)
            {
                return new ActionProgressSummary(string.Empty, false, -1, new List<ActionPhaseProgress>());
            }

            var task = FindTaskById(characterState.CurrentTaskId);
            return BuildActionProgressSummary(characterState, task, characterState.TravelDurationSeconds, true);
        }

        public ActionProgressSummary GetTaskPlanPreview(string characterId, TaskDefinition task)
        {
            if (runState == null || string.IsNullOrEmpty(characterId) || task == null)
            {
                return new ActionProgressSummary(string.Empty, false, -1, new List<ActionPhaseProgress>());
            }

            var characterState = runState.GetCharacter(characterId);
            var travelDuration = GetEstimatedTravelDuration(task);
            return BuildActionProgressSummary(characterState, task, travelDuration, false);
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

        private TaskDefinition FindTaskById(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                return null;
            }

            foreach (var task in tasks)
            {
                if (task != null && task.TaskId == taskId)
                {
                    return task;
                }
            }

            return null;
        }

        private ActionProgressSummary BuildActionProgressSummary(
            CharacterState characterState,
            TaskDefinition task,
            float travelDurationSeconds,
            bool includeActiveProgress)
        {
            if (characterState == null || task == null)
            {
                return new ActionProgressSummary(string.Empty, false, -1, new List<ActionPhaseProgress>());
            }

            var phases = new List<ActionPhaseProgress>();
            var hasWorkPhase = !task.CompleteOnArrival;
            var travelDuration = Mathf.Max(travelDurationSeconds, 0.01f);
            var isReturnTravel = task.CompleteOnArrival &&
                task.RequiredLocation != null &&
                task.RequiredLocation.LocationId == characterState.HomeLocationId &&
                characterState.CurrentLocationId != characterState.HomeLocationId;
            var travelLabel = isReturnTravel ? "Return Travel" : "Travel / Preparation";
            var travelType = isReturnTravel ? ActionPhaseType.ReturnTravel : ActionPhaseType.TravelPreparation;

            var travelElapsed = 0f;
            var workElapsed = 0f;
            var currentPhaseIndex = -1;

            if (includeActiveProgress)
            {
                if (characterState.IsTravelling)
                {
                    travelElapsed = Mathf.Clamp01(characterState.TravelProgress) * travelDuration;
                    currentPhaseIndex = 0;
                }
                else
                {
                    travelElapsed = travelDuration;
                }
            }

            phases.Add(new ActionPhaseProgress(travelType, travelLabel, travelDuration, travelElapsed));

            if (hasWorkPhase)
            {
                var workDuration = Mathf.Max(characterState.TaskDurationSeconds > 0f
                    ? characterState.TaskDurationSeconds
                    : task.DurationSeconds, 0.01f);

                if (includeActiveProgress && characterState.IsWorkingOnTask)
                {
                    workElapsed = Mathf.Clamp(characterState.TaskElapsedSeconds, 0f, workDuration);
                    currentPhaseIndex = 1;
                }

                phases.Add(new ActionPhaseProgress(ActionPhaseType.Work, "Work / Main Action", workDuration, workElapsed));
            }

            if (!includeActiveProgress)
            {
                currentPhaseIndex = phases.Count > 0 ? 0 : -1;
            }
            else if (currentPhaseIndex < 0 && phases.Count > 0)
            {
                currentPhaseIndex = Mathf.Min(phases.Count - 1, hasWorkPhase ? 1 : 0);
            }

            return new ActionProgressSummary(task.TaskId, includeActiveProgress && characterState.HasActiveTask, currentPhaseIndex, phases);
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
