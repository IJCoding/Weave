using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;

namespace Weave.Simulation
{
    public readonly struct TravelCommand
    {
        public TravelCommand(string characterId, string originLocationId, string destinationLocationId)
        {
            CharacterId = characterId;
            OriginLocationId = originLocationId;
            DestinationLocationId = destinationLocationId;
        }

        public string CharacterId { get; }
        public string OriginLocationId { get; }
        public string DestinationLocationId { get; }
    }

    public readonly struct EventResolution
    {
        public EventResolution(string optionId, string summaryText)
        {
            OptionId = optionId;
            SummaryText = summaryText;
        }

        public string OptionId { get; }
        public string SummaryText { get; }
    }

    public readonly struct SimulationAdvanceResult
    {
        public SimulationAdvanceResult(
            bool stateChanged,
            bool dayAdvanced,
            bool taskCompleted,
            string completedTaskId,
            bool taskInterrupted,
            string interruptedTaskId,
            EventDefinition completedTaskFollowUpEvent,
            IReadOnlyList<SimulationLogSignal> logSignals)
        {
            StateChanged = stateChanged;
            DayAdvanced = dayAdvanced;
            TaskCompleted = taskCompleted;
            CompletedTaskId = completedTaskId;
            TaskInterrupted = taskInterrupted;
            InterruptedTaskId = interruptedTaskId;
            CompletedTaskFollowUpEvent = completedTaskFollowUpEvent;
            LogSignals = logSignals ?? new List<SimulationLogSignal>();
        }

        public bool StateChanged { get; }
        public bool DayAdvanced { get; }
        public bool TaskCompleted { get; }
        public string CompletedTaskId { get; }
        public bool TaskInterrupted { get; }
        public string InterruptedTaskId { get; }
        public EventDefinition CompletedTaskFollowUpEvent { get; }
        public IReadOnlyList<SimulationLogSignal> LogSignals { get; }
    }

    public sealed class DaySimulationService
    {
        private const float MinimumDurationSeconds = 0.01f;
        private readonly CanonResolver canonResolver;

        public DaySimulationService(CanonResolver canonResolver)
        {
            this.canonResolver = canonResolver;
        }

        public RunState CreateInitialState(
            GameCalendarDefinition calendar,
            IEnumerable<LocationDefinition> locations,
            IEnumerable<CharacterDefinition> characters,
            CharacterDefinition controlledCharacter)
        {
            var runState = new RunState
            {
                ControlledCharacterId = controlledCharacter != null ? controlledCharacter.CharacterId : string.Empty,
                Calendar = new CalendarState(calendar.StartingYear),
                DayTimer = new DayTimerState(GetConfiguredDayDuration(calendar))
            };

            foreach (var location in locations)
            {
                runState.Locations[location.LocationId] = new LocationState(location.LocationId);
            }

            foreach (var character in characters)
            {
                runState.Characters[character.CharacterId] = new CharacterState(character);
            }

            return runState;
        }

        public List<TaskDefinition> GetAvailableTasks(
            CharacterDefinition character,
            IEnumerable<TaskDefinition> tasks,
            RunState runState)
        {
            var available = new List<TaskDefinition>();

            foreach (var task in tasks)
            {
                if (!IsTaskAvailable(character, task, runState))
                {
                    continue;
                }

                available.Add(task);
            }

            return available;
        }

        public bool IsTaskAvailable(CharacterDefinition character, TaskDefinition task, RunState runState)
        {
            if (task == null || character == null || runState == null)
            {
                return false;
            }

            if (!task.IsAvailableToAllCharacters && !IsEligibleCharacter(task, character))
            {
                return false;
            }

            foreach (var requiredFlag in task.RequiredWorldFlags)
            {
                if (!runState.HasWorldFlag(requiredFlag))
                {
                    return false;
                }
            }

            foreach (var blockedFlag in task.BlockedWorldFlags)
            {
                if (runState.HasWorldFlag(blockedFlag))
                {
                    return false;
                }
            }

            var characterState = runState.GetCharacter(character.CharacterId);

            if (task.UnavailableWhenAlreadyAtRequiredLocation &&
                task.RequiredLocation != null &&
                characterState.CurrentLocationId == task.RequiredLocation.LocationId)
            {
                return false;
            }

            return task.RequiredLocation != null;
        }

        public float EstimateTravelDuration(
            RunState runState,
            CharacterDefinition character,
            TaskDefinition task,
            IEnumerable<LocationDefinition> locations,
            IEnumerable<ResourceDefinition> resources,
            float secondsPerDistanceUnit,
            float sameLocationPreparationSeconds,
            float carryPenaltyPerWeightUnit)
        {
            if (runState == null || character == null || task == null || task.RequiredLocation == null)
            {
                return MinimumDurationSeconds;
            }

            var characterState = runState.GetCharacter(character.CharacterId);
            var origin = GetLocationPosition(characterState.CurrentLocationId, locations);
            var destination = GetLocationPosition(task.RequiredLocation.LocationId, locations);
            var distance = Vector2.Distance(origin, destination);
            var baseDuration = distance <= Mathf.Epsilon
                ? Mathf.Max(sameLocationPreparationSeconds, MinimumDurationSeconds)
                : Mathf.Max(distance * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds), MinimumDurationSeconds);

            if (distance <= Mathf.Epsilon)
            {
                return baseDuration;
            }

            var carriedWeight = GetCarriedWeight(characterState, resources);
            var penaltyMultiplier = 1f + Mathf.Max(0f, carriedWeight) * Mathf.Max(0f, carryPenaltyPerWeightUnit);
            return Mathf.Max(baseDuration * penaltyMultiplier, MinimumDurationSeconds);
        }

        public float EstimateTravelDurationToLocation(
            RunState runState,
            CharacterDefinition character,
            string destinationLocationId,
            IEnumerable<LocationDefinition> locations,
            IEnumerable<ResourceDefinition> resources,
            float secondsPerDistanceUnit,
            float carryPenaltyPerWeightUnit)
        {
            if (runState == null || character == null || string.IsNullOrEmpty(destinationLocationId))
            {
                return MinimumDurationSeconds;
            }

            if (!runState.Characters.TryGetValue(character.CharacterId, out var characterState) || characterState == null)
            {
                return MinimumDurationSeconds;
            }

            var origin = GetLocationPosition(characterState.CurrentLocationId, locations);
            var destination = GetLocationPosition(destinationLocationId, locations);
            var distance = Vector2.Distance(origin, destination);

            if (distance <= Mathf.Epsilon)
            {
                return MinimumDurationSeconds;
            }

            var baseDuration = Mathf.Max(distance * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds), MinimumDurationSeconds);
            var carriedWeight = GetCarriedWeight(characterState, resources);
            var penaltyMultiplier = 1f + Mathf.Max(0f, carriedWeight) * Mathf.Max(0f, carryPenaltyPerWeightUnit);
            return Mathf.Max(baseDuration * penaltyMultiplier, MinimumDurationSeconds);
        }

        public TravelCommand StartTravel(
            RunState runState,
            CharacterDefinition character,
            TaskDefinition task,
            float travelDurationSeconds)
        {
            if (!IsTaskAvailable(character, task, runState))
            {
                return default;
            }

            var characterState = runState.GetCharacter(character.CharacterId);
            if (characterState.IsTravelling || characterState.IsWorkingOnTask)
            {
                return default;
            }

            characterState.TravelOriginLocationId = characterState.CurrentLocationId;
            characterState.TravelDestinationLocationId = task.RequiredLocation.LocationId;
            characterState.TravelProgress = 0f;
            characterState.CurrentTaskId = task.TaskId;
            characterState.CurrentTaskPhase = TaskPhase.Travelling;
            characterState.TravelDurationSeconds = Mathf.Max(travelDurationSeconds, MinimumDurationSeconds);
            characterState.TaskElapsedSeconds = 0f;
            characterState.TaskDurationSeconds = Mathf.Max(task.DurationSeconds, MinimumDurationSeconds);
            characterState.CompleteTaskOnArrival = task.CompleteOnArrival;

            return new TravelCommand(
                character.CharacterId,
                characterState.TravelOriginLocationId,
                characterState.TravelDestinationLocationId);
        }

        public TravelCommand StartTravelToLocation(
            RunState runState,
            CharacterDefinition character,
            string destinationLocationId,
            float travelDurationSeconds)
        {
            if (runState == null ||
                character == null ||
                string.IsNullOrEmpty(destinationLocationId))
            {
                return default;
            }

            if (!runState.Characters.TryGetValue(character.CharacterId, out var characterState) || characterState == null)
            {
                return default;
            }

            if (characterState.IsTravelling ||
                characterState.IsWorkingOnTask ||
                characterState.CurrentLocationId == destinationLocationId)
            {
                return default;
            }

            ClearTaskState(characterState);
            characterState.TravelOriginLocationId = characterState.CurrentLocationId;
            characterState.TravelDestinationLocationId = destinationLocationId;
            characterState.TravelProgress = 0f;
            characterState.CurrentTaskPhase = TaskPhase.Travelling;
            characterState.TravelDurationSeconds = Mathf.Max(travelDurationSeconds, MinimumDurationSeconds);

            return new TravelCommand(
                character.CharacterId,
                characterState.TravelOriginLocationId,
                characterState.TravelDestinationLocationId);
        }

        public bool TickTravel(RunState runState, string characterId, float step)
        {
            if (runState == null || step <= 0f)
            {
                return false;
            }

            var characterState = runState.GetCharacter(characterId);

            if (!characterState.IsTravelling)
            {
                return false;
            }

            AdvanceTravel(characterState, step);

            if (characterState.TravelProgress >= 1f)
            {
                CompleteTravelPhase(characterState);
            }

            return true;
        }

        public bool ResolveTask(RunState runState, CharacterDefinition actor, TaskDefinition task)
        {
            if (!IsTaskAvailable(actor, task, runState))
            {
                return false;
            }

            var actorState = runState.GetCharacter(actor.CharacterId);

            if (actorState.IsTravelling ||
                !actorState.IsWorkingOnTask ||
                actorState.CurrentLocationId != task.RequiredLocation.LocationId ||
                actorState.CurrentTaskId != task.TaskId ||
                actorState.TaskElapsedSeconds < actorState.TaskDurationSeconds)
            {
                return false;
            }

            foreach (var change in task.ActorResourceChanges)
            {
                if (task.RewardsAddedToCarriedResources)
                {
                    actorState.ChangeCarriedResource(change.ResourceId, change.Amount);
                    continue;
                }

                actorState.ChangeStoredResource(change.ResourceId, change.Amount);
            }

            ClearTaskState(actorState);
            return true;
        }

        public SimulationAdvanceResult AdvanceSimulation(
            RunState runState,
            GameCalendarDefinition calendar,
            CharacterDefinition controlledCharacter,
            IEnumerable<TaskDefinition> tasks,
            IEnumerable<LocationDefinition> locations,
            float simulationSeconds)
        {
            if (runState == null || calendar == null || controlledCharacter == null || simulationSeconds <= 0f)
            {
                return default;
            }

            EnsureDayTimer(runState, calendar);

            var stateChanged = false;
            var dayAdvanced = false;
            var taskCompleted = false;
            var completedTaskId = string.Empty;
            var taskInterrupted = false;
            var interruptedTaskId = string.Empty;
            EventDefinition followUpEvent = null;
            var remainingSeconds = simulationSeconds;
            var controlledState = runState.GetCharacter(controlledCharacter.CharacterId);
            var logSignals = new List<SimulationLogSignal>();

            while (remainingSeconds > 0f)
            {
                var taskDefinition = GetTaskById(controlledState.CurrentTaskId, tasks);
                var timeToTaskBoundary = GetTimeToTaskBoundary(controlledState);
                var timeToDayBoundary = runState.DayTimer.RemainingSeconds;
                var step = remainingSeconds;

                if (timeToDayBoundary > 0f)
                {
                    step = Mathf.Min(step, timeToDayBoundary);
                }

                if (timeToTaskBoundary > 0f)
                {
                    step = Mathf.Min(step, timeToTaskBoundary);
                }

                if (step > 0f)
                {
                    runState.DayTimer.RemainingSeconds = Mathf.Max(0f, runState.DayTimer.RemainingSeconds - step);

                    if (controlledState.IsTravelling)
                    {
                        var travelStep = step / Mathf.Max(controlledState.TravelDurationSeconds, MinimumDurationSeconds);
                        AdvanceTravel(controlledState, travelStep);
                    }
                    else if (controlledState.IsWorkingOnTask)
                    {
                        controlledState.TaskElapsedSeconds = Mathf.Min(
                            controlledState.TaskDurationSeconds,
                            controlledState.TaskElapsedSeconds + step);
                    }

                    remainingSeconds -= step;
                    stateChanged = true;
                }

                if (controlledState.IsTravelling &&
                    controlledState.TravelProgress >= 1f)
                {
                    var wasTravelOnly = controlledState.CompleteTaskOnArrival;
                    var travelCompletion = CompleteTravelPhase(controlledState);
                    var destinationName = taskDefinition?.RequiredLocation != null
                        ? taskDefinition.RequiredLocation.DisplayName
                        : GetLocationDisplayName(travelCompletion.DestinationLocationId, locations);

                    if (travelCompletion.OriginLocationId == travelCompletion.DestinationLocationId)
                    {
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Travel,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} completed preparation at {destinationName}."));
                    }
                    else
                    {
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Travel,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} arrived at {destinationName}."));
                    }

                    if (!string.IsNullOrEmpty(travelCompletion.DepositedSummary))
                    {
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Resource,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} deposited {travelCompletion.DepositedSummary}."));
                    }

                    if (wasTravelOnly && taskDefinition != null)
                    {
                        taskCompleted = true;
                        completedTaskId = taskDefinition.TaskId;
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Work,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} completed {taskDefinition.DisplayName}."));
                    }
                    else if (!wasTravelOnly && taskDefinition != null)
                    {
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Work,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} began {taskDefinition.DisplayName}."));
                    }

                    stateChanged = true;
                    continue;
                }

                if (controlledState.IsWorkingOnTask &&
                    controlledState.TaskElapsedSeconds >= controlledState.TaskDurationSeconds &&
                    taskDefinition != null)
                {
                    if (ResolveTask(runState, controlledCharacter, taskDefinition))
                    {
                        taskCompleted = true;
                        completedTaskId = taskDefinition.TaskId;
                        followUpEvent = taskDefinition.FollowUpEvent;
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.Work,
                            controlledCharacter.CharacterId,
                            $"{controlledCharacter.DisplayName} completed {taskDefinition.DisplayName}."));
                        LogResourceChanges(logSignals, controlledCharacter, taskDefinition);
                        stateChanged = true;
                    }

                    continue;
                }

                if (runState.DayTimer.RemainingSeconds <= 0f)
                {
                    logSignals.Add(new SimulationLogSignal(
                        SimulationLogCategory.System,
                        controlledCharacter.CharacterId,
                        $"Day {runState.Calendar.DayOfSeason} ended."));

                    if (controlledState.IsWorkingOnTask && !string.IsNullOrEmpty(controlledState.CurrentTaskId))
                    {
                        taskInterrupted = true;
                        interruptedTaskId = controlledState.CurrentTaskId;
                        logSignals.Add(new SimulationLogSignal(
                            SimulationLogCategory.System,
                            controlledCharacter.CharacterId,
                            $"{GetTaskById(interruptedTaskId, tasks)?.DisplayName ?? interruptedTaskId} was interrupted when the day ended."));
                    }

                    AdvanceDay(runState, calendar);
                    dayAdvanced = true;
                    stateChanged = true;
                    logSignals.Add(new SimulationLogSignal(
                        SimulationLogCategory.System,
                        controlledCharacter.CharacterId,
                        $"Day {runState.Calendar.DayOfSeason} began."));
                    continue;
                }

                break;
            }

            return new SimulationAdvanceResult(
                stateChanged,
                dayAdvanced,
                taskCompleted,
                completedTaskId,
                taskInterrupted,
                interruptedTaskId,
                followUpEvent,
                logSignals);
        }

        public EventResolution ResolveNpcEvent(
            RunState runState,
            PlayerCanonState playerCanon,
            EventDefinition eventDefinition)
        {
            if (eventDefinition == null || eventDefinition.DecisionMaker == null)
            {
                return new EventResolution(string.Empty, string.Empty);
            }

            var optionId = canonResolver.ResolveOption(
                eventDefinition.DecisionMaker,
                eventDefinition.DecisionKey,
                playerCanon);

            return ResolveEvent(runState, eventDefinition, optionId);
        }

        public EventResolution ResolveEvent(
            RunState runState,
            EventDefinition eventDefinition,
            string selectedOptionId)
        {
            if (runState == null || eventDefinition == null || string.IsNullOrWhiteSpace(selectedOptionId))
            {
                return new EventResolution(string.Empty, string.Empty);
            }

            foreach (var option in eventDefinition.Options)
            {
                if (option.OptionId != selectedOptionId)
                {
                    continue;
                }

                foreach (var outcome in option.Outcomes)
                {
                    if (!ConditionsMatch(runState, outcome.Conditions))
                    {
                        continue;
                    }

                    ApplyOutcome(runState, outcome);
                    return new EventResolution(option.OptionId, outcome.SummaryText);
                }

                return new EventResolution(option.OptionId, string.Empty);
            }

            return new EventResolution(string.Empty, string.Empty);
        }

        public void AdvanceDay(RunState runState, GameCalendarDefinition calendar)
        {
            foreach (var characterState in runState.Characters.Values)
            {
                ClearTaskState(characterState);
                characterState.TravelProgress = 0f;
                characterState.TravelOriginLocationId = characterState.CurrentLocationId;
                characterState.TravelDestinationLocationId = string.Empty;
                characterState.TravelDurationSeconds = 0f;
            }

            runState.Calendar.Advance(calendar);
            EnsureDayTimer(runState, calendar);
            runState.DayTimer.Reset(GetConfiguredDayDuration(calendar));
        }

        private static void EnsureDayTimer(RunState runState, GameCalendarDefinition calendar)
        {
            if (runState.DayTimer == null)
            {
                runState.DayTimer = new DayTimerState(GetConfiguredDayDuration(calendar));
            }
        }

        private static float GetConfiguredDayDuration(GameCalendarDefinition calendar)
        {
            return Mathf.Max(calendar != null ? calendar.DayDurationSeconds : 0f, 1f);
        }

        private static void AdvanceTravel(CharacterState characterState, float step)
        {
            characterState.TravelProgress = Mathf.Min(1f, characterState.TravelProgress + step);
        }

        private static float GetTimeToTaskBoundary(CharacterState characterState)
        {
            if (characterState.IsTravelling)
            {
                var remainingProgress = Mathf.Max(0f, 1f - characterState.TravelProgress);
                return remainingProgress * Mathf.Max(characterState.TravelDurationSeconds, MinimumDurationSeconds);
            }

            if (characterState.IsWorkingOnTask)
            {
                return Mathf.Max(0f, characterState.TaskDurationSeconds - characterState.TaskElapsedSeconds);
            }

            return float.PositiveInfinity;
        }

        private static TaskDefinition GetTaskById(string taskId, IEnumerable<TaskDefinition> tasks)
        {
            if (string.IsNullOrEmpty(taskId) || tasks == null)
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

        private static void ClearTaskState(CharacterState characterState)
        {
            characterState.CurrentTaskId = string.Empty;
            characterState.CurrentTaskPhase = TaskPhase.None;
            characterState.TaskElapsedSeconds = 0f;
            characterState.TaskDurationSeconds = 0f;
            characterState.CompleteTaskOnArrival = false;
        }

        private static bool ConditionsMatch(RunState runState, IReadOnlyList<WorldFlagRequirement> requirements)
        {
            foreach (var requirement in requirements)
            {
                var hasFlag = runState.HasWorldFlag(requirement.FlagId);

                if (hasFlag != requirement.MustBePresent)
                {
                    return false;
                }
            }

            return true;
        }

        private static void ApplyOutcome(RunState runState, OutcomeVariantDefinition outcome)
        {
            foreach (var mutation in outcome.WorldFlagMutations)
            {
                runState.SetWorldFlag(mutation.FlagId, mutation.SetPresent);
            }

            foreach (var resourceChange in outcome.ResourceChanges)
            {
                if (resourceChange.Character == null)
                {
                    continue;
                }

                var targetState = runState.GetCharacter(resourceChange.Character.CharacterId);
                targetState.ChangeStoredResource(resourceChange.ResourceId, resourceChange.Amount);
            }
        }

        private static bool IsEligibleCharacter(TaskDefinition task, CharacterDefinition character)
        {
            foreach (var eligibleCharacter in task.EligibleCharacters)
            {
                if (eligibleCharacter == character)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 GetLocationPosition(string locationId, IEnumerable<LocationDefinition> locations)
        {
            if (locations == null || string.IsNullOrEmpty(locationId))
            {
                return Vector2.zero;
            }

            foreach (var location in locations)
            {
                if (location != null && location.LocationId == locationId)
                {
                    return location.MapPosition;
                }
            }

            return Vector2.zero;
        }

        private static float GetCarriedWeight(CharacterState characterState, IEnumerable<ResourceDefinition> resources)
        {
            var weights = BuildResourceWeightLookup(resources);
            var totalWeight = 0f;

            foreach (var entry in characterState.CarriedResources)
            {
                if (entry.Value <= 0)
                {
                    continue;
                }

                if (!weights.TryGetValue(entry.Key, out var weightPerUnit))
                {
                    weightPerUnit = 1f;
                }

                totalWeight += entry.Value * Mathf.Max(0f, weightPerUnit);
            }

            return totalWeight;
        }

        private static Dictionary<string, float> BuildResourceWeightLookup(IEnumerable<ResourceDefinition> resources)
        {
            var lookup = new Dictionary<string, float>();

            if (resources == null)
            {
                return lookup;
            }

            foreach (var resource in resources)
            {
                if (resource == null || string.IsNullOrEmpty(resource.ResourceId))
                {
                    continue;
                }

                lookup[resource.ResourceId] = resource.CarryWeightPerUnit;
            }

            return lookup;
        }

        private static TravelPhaseCompletion CompleteTravelPhase(CharacterState characterState)
        {
            var originLocationId = characterState.TravelOriginLocationId;
            var destinationLocationId = characterState.TravelDestinationLocationId;
            characterState.TravelProgress = 0f;
            characterState.CurrentLocationId = characterState.TravelDestinationLocationId;
            characterState.TravelOriginLocationId = characterState.CurrentLocationId;
            characterState.TravelDestinationLocationId = string.Empty;
            characterState.TravelDurationSeconds = 0f;

            var depositedSummary = DepositCarriedResourcesIfArrivedHomeFromAway(characterState, originLocationId);

            if (characterState.CompleteTaskOnArrival)
            {
                ClearTaskState(characterState);
                return new TravelPhaseCompletion(
                    originLocationId,
                    destinationLocationId,
                    depositedSummary);
            }

            if (!characterState.HasActiveTask)
            {
                characterState.CurrentTaskPhase = TaskPhase.None;
                return new TravelPhaseCompletion(
                    originLocationId,
                    destinationLocationId,
                    depositedSummary);
            }

            characterState.CurrentTaskPhase = TaskPhase.Working;
            return new TravelPhaseCompletion(
                originLocationId,
                destinationLocationId,
                depositedSummary);
        }

        private static string GetLocationDisplayName(string locationId, IEnumerable<LocationDefinition> locations)
        {
            if (string.IsNullOrEmpty(locationId))
            {
                return "destination";
            }

            if (locations != null)
            {
                foreach (var location in locations)
                {
                    if (location != null && location.LocationId == locationId)
                    {
                        return string.IsNullOrEmpty(location.DisplayName) ? locationId : location.DisplayName;
                    }
                }
            }

            return locationId;
        }

        private static string DepositCarriedResourcesIfArrivedHomeFromAway(CharacterState characterState, string originLocationId)
        {
            if (string.IsNullOrEmpty(characterState.HomeLocationId) ||
                characterState.CurrentLocationId != characterState.HomeLocationId ||
                originLocationId == characterState.HomeLocationId)
            {
                return string.Empty;
            }

            var summaryParts = new List<string>();

            foreach (var carriedResource in characterState.CarriedResources)
            {
                if (carriedResource.Value <= 0)
                {
                    continue;
                }

                characterState.ChangeStoredResource(carriedResource.Key, carriedResource.Value);
                summaryParts.Add($"{carriedResource.Key} +{carriedResource.Value}");
            }

            characterState.CarriedResources.Clear();
            return BuildResourceSummary(summaryParts);
        }

        private static void LogResourceChanges(
            List<SimulationLogSignal> logSignals,
            CharacterDefinition character,
            TaskDefinition taskDefinition)
        {
            foreach (var change in taskDefinition.ActorResourceChanges)
            {
                if (change.Amount <= 0)
                {
                    continue;
                }

                if (taskDefinition.RewardsAddedToCarriedResources)
                {
                    logSignals.Add(new SimulationLogSignal(
                        SimulationLogCategory.Resource,
                        character.CharacterId,
                        $"{character.DisplayName} gathered {change.Amount} {change.ResourceId} and is carrying it."));
                    continue;
                }

                logSignals.Add(new SimulationLogSignal(
                    SimulationLogCategory.Resource,
                    character.CharacterId,
                    $"{character.DisplayName} stored {change.Amount} {change.ResourceId}."));
            }
        }

        private static string BuildResourceSummary(List<string> summaryParts)
        {
            if (summaryParts.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(", ", summaryParts);
        }

        private readonly struct TravelPhaseCompletion
        {
            public TravelPhaseCompletion(string originLocationId, string destinationLocationId, string depositedSummary)
            {
                OriginLocationId = originLocationId;
                DestinationLocationId = destinationLocationId;
                DepositedSummary = depositedSummary;
            }

            public string OriginLocationId { get; }
            public string DestinationLocationId { get; }
            public string DepositedSummary { get; }
        }
    }
}
