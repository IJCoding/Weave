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
        Work
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
            TotalDurationSeconds <= Mathf.Epsilon
                ? (HasPhases ? 1f : 0f)
                : Mathf.Clamp01(CompletedDurationSeconds / TotalDurationSeconds);
        public float CurrentPhaseProgress => CurrentPhase.DurationSeconds <= Mathf.Epsilon
            ? (CurrentPhaseIndex >= 0 ? 1f : 0f)
            : Mathf.Clamp01(CurrentPhase.ElapsedSeconds / CurrentPhase.DurationSeconds);
        public float CurrentPhaseRemainingSeconds =>
            Mathf.Max(0f, CurrentPhase.DurationSeconds - CurrentPhase.ElapsedSeconds);
    }

    public sealed class PrototypeGameSession : MonoBehaviour
    {
        private const int MaxSimulationLogEntries = 50;
        [SerializeField] private GameCalendarDefinition calendarDefinition;
        [SerializeField] private List<LocationDefinition> locations = new List<LocationDefinition>();
        [SerializeField] private List<CharacterDefinition> characters = new List<CharacterDefinition>();
        [SerializeField] private List<TaskDefinition> tasks = new List<TaskDefinition>();
        [SerializeField] private List<ResourceDefinition> resources = new List<ResourceDefinition>();
        [SerializeField] private float secondsPerDistanceUnit = 3f;
        [SerializeField] private float sameLocationPreparationSeconds = 1f;
        [SerializeField] private float carryPenaltyPerWeightUnit = 0.05f;

        private readonly DaySimulationService simulation = new DaySimulationService(new CanonResolver());
        private readonly List<SimulationLogEntry> simulationLogEntries = new List<SimulationLogEntry>();
        private RunState runState;
        private SimulationSpeedMode selectedSpeedMode = SimulationSpeedMode.Normal;
        private int pauseOverrideDepth;

        public event Action StateChanged;
        public event Action<SimulationAdvanceResult> SimulationAdvanced;
        public event Action<SimulationLogEntry> SimulationLogEntryAdded;

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
        public IReadOnlyList<SimulationLogEntry> SimulationLogEntries => simulationLogEntries;

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
            simulationLogEntries.Clear();
            pauseOverrideDepth = 0;
            selectedSpeedMode = SimulationSpeedMode.Normal;
            AppendLog(
                SimulationLogCategory.System,
                controlledCharacter != null ? controlledCharacter.CharacterId : string.Empty,
                $"Day {runState.Calendar.DayOfSeason} began.");
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

            var availableTasks = simulation.GetAvailableTasks(GetControlledCharacter(), tasks, runState);
            var controlledState = runState.GetCharacter(runState.ControlledCharacterId);
            var currentLocationId = controlledState.CurrentLocationId;
            var filtered = new List<TaskDefinition>();

            foreach (var task in availableTasks)
            {
                if (task == null ||
                    task.CompleteOnArrival ||
                    task.RequiredLocation == null ||
                    task.RequiredLocation.LocationId != currentLocationId)
                {
                    continue;
                }

                filtered.Add(task);
            }

            return filtered;
        }

        public TravelCommand AssignPlayerTask(TaskDefinition task)
        {
            if (runState == null)
            {
                return default;
            }

            var character = GetControlledCharacter();
            var characterState = character != null ? runState.GetCharacter(character.CharacterId) : null;
            if (task == null ||
                character == null ||
                characterState == null ||
                task.RequiredLocation == null ||
                task.RequiredLocation.LocationId != characterState.CurrentLocationId)
            {
                return default;
            }

            var command = simulation.StartTravel(
                runState,
                character,
                task,
                GetEstimatedTravelDuration(task));

            if (!string.IsNullOrEmpty(command.CharacterId) &&
                task != null &&
                task.RequiredLocation != null &&
                characterState != null &&
                character != null)
            {
                AppendLog(
                    SimulationLogCategory.Travel,
                    character.CharacterId,
                    $"{character.DisplayName} began preparing for {task.DisplayName}.");
            }

            NotifyStateChanged();
            return command;
        }

        public float GetEstimatedTravelDurationToLocation(string locationId)
        {
            if (runState == null || string.IsNullOrEmpty(locationId))
            {
                return 0f;
            }

            return simulation.EstimateTravelDurationToLocation(
                runState,
                GetControlledCharacter(),
                locationId,
                locations,
                resources,
                SecondsPerDistanceUnit,
                CarryPenaltyPerWeightUnit);
        }

        public TravelCommand RequestPlayerTravel(string locationId)
        {
            if (runState == null || string.IsNullOrEmpty(locationId))
            {
                return default;
            }

            var character = GetControlledCharacter();
            var destination = FindLocationById(locationId);

            if (character == null || destination == null)
            {
                return default;
            }

            var command = simulation.StartTravelToLocation(
                runState,
                character,
                destination.LocationId,
                GetEstimatedTravelDurationToLocation(destination.LocationId));

            if (!string.IsNullOrEmpty(command.CharacterId))
            {
                AppendLog(
                    SimulationLogCategory.Travel,
                    character.CharacterId,
                    $"{character.DisplayName} left {GetLocationDisplayName(command.OriginLocationId)} for {GetLocationDisplayName(command.DestinationLocationId)}.");
                NotifyStateChanged();
            }

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
                locations,
                simulationSeconds);

            if (result.StateChanged)
            {
                AppendSignals(result.LogSignals);
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
            if (eventDefinition != null)
            {
                var actor = eventDefinition.DecisionMaker != null ? eventDefinition.DecisionMaker.CharacterId : string.Empty;
                var title = string.IsNullOrEmpty(eventDefinition.Title) ? eventDefinition.EventId : eventDefinition.Title;
                AppendLog(
                    SimulationLogCategory.Decision,
                    actor,
                    $"Decision made for {title}: {selectedOptionId}.");

                if (!string.IsNullOrEmpty(resolution.SummaryText))
                {
                    AppendLog(SimulationLogCategory.Event, actor, resolution.SummaryText);
                }
            }

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
            if (eventDefinition != null)
            {
                var actor = eventDefinition.DecisionMaker != null ? eventDefinition.DecisionMaker.CharacterId : string.Empty;
                var title = string.IsNullOrEmpty(eventDefinition.Title) ? eventDefinition.EventId : eventDefinition.Title;
                AppendLog(
                    SimulationLogCategory.Decision,
                    actor,
                    $"NPC decision resolved for {title}.");

                if (!string.IsNullOrEmpty(resolution.SummaryText))
                {
                    AppendLog(SimulationLogCategory.Event, actor, resolution.SummaryText);
                }
            }

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

        public void LogDecisionRequested(EventDefinition eventDefinition)
        {
            if (runState == null || eventDefinition == null)
            {
                return;
            }

            var actorId = eventDefinition.DecisionMaker != null
                ? eventDefinition.DecisionMaker.CharacterId
                : runState.ControlledCharacterId;
            var sourceLabel = !string.IsNullOrEmpty(eventDefinition.SourceLabel)
                ? eventDefinition.SourceLabel
                : eventDefinition.DecisionMaker != null
                    ? eventDefinition.DecisionMaker.DisplayName
                    : "System";
            var title = string.IsNullOrEmpty(eventDefinition.Title)
                ? eventDefinition.EventId
                : eventDefinition.Title;
            AppendLog(
                SimulationLogCategory.Event,
                actorId,
                $"{sourceLabel} requested a decision: {title}.");
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
            var character = FindCharacterById(characterId);
            if (character == null)
            {
                return new ActionProgressSummary(string.Empty, false, -1, new List<ActionPhaseProgress>());
            }

            var travelDuration = simulation.EstimateTravelDuration(
                runState,
                character,
                task,
                locations,
                resources,
                SecondsPerDistanceUnit,
                SameLocationPreparationSeconds,
                CarryPenaltyPerWeightUnit);
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

        private LocationDefinition FindLocationById(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
            {
                return null;
            }

            foreach (var location in locations)
            {
                if (location != null && location.LocationId == locationId)
                {
                    return location;
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

        private CharacterDefinition FindCharacterById(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return null;
            }

            foreach (var character in characters)
            {
                if (character != null && character.CharacterId == characterId)
                {
                    return character;
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
            var travelDuration = Mathf.Max(travelDurationSeconds, 0f);
            var travelLabel = "Travel / Preparation";

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

            phases.Add(new ActionPhaseProgress(ActionPhaseType.TravelPreparation, travelLabel, travelDuration, travelElapsed));

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

        private void AppendSignals(IReadOnlyList<SimulationLogSignal> signals)
        {
            if (signals == null)
            {
                return;
            }

            foreach (var signal in signals)
            {
                AppendLog(signal.Category, signal.CharacterId, signal.Message);
            }
        }

        private void AppendLog(SimulationLogCategory category, string characterId, string message)
        {
            if (runState == null || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var dayElapsedSeconds = runState.DayTimer != null
                ? Mathf.Max(0f, runState.DayTimer.DurationSeconds - runState.DayTimer.RemainingSeconds)
                : 0f;
            var entry = new SimulationLogEntry(
                runState.Calendar != null ? runState.Calendar.DayOfSeason : 1,
                dayElapsedSeconds,
                category,
                characterId,
                message);
            simulationLogEntries.Add(entry);

            while (simulationLogEntries.Count > MaxSimulationLogEntries)
            {
                simulationLogEntries.RemoveAt(0);
            }

            SimulationLogEntryAdded?.Invoke(entry);
        }

        private string GetLocationDisplayName(string locationId)
        {
            if (string.IsNullOrEmpty(locationId))
            {
                return "Unknown";
            }

            foreach (var location in locations)
            {
                if (location != null && location.LocationId == locationId)
                {
                    return string.IsNullOrEmpty(location.DisplayName) ? locationId : location.DisplayName;
                }
            }

            return locationId;
        }
    }
}
