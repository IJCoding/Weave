using System.Collections.Generic;
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

    public sealed class DaySimulationService
    {
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
                Calendar = new CalendarState(calendar.StartingYear)
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

            return task.RequiredLocation != null;
        }

        public TravelCommand StartTravel(RunState runState, CharacterDefinition character, TaskDefinition task)
        {
            if (!IsTaskAvailable(character, task, runState))
            {
                return default;
            }

            var characterState = runState.GetCharacter(character.CharacterId);
            characterState.TravelOriginLocationId = characterState.CurrentLocationId;
            characterState.TravelDestinationLocationId = task.RequiredLocation.LocationId;
            characterState.TravelProgress = 0f;
            characterState.CurrentTaskId = task.TaskId;

            return new TravelCommand(
                character.CharacterId,
                characterState.TravelOriginLocationId,
                characterState.TravelDestinationLocationId);
        }

        public void TickTravel(RunState runState, string characterId, float step)
        {
            var characterState = runState.GetCharacter(characterId);

            if (characterState.TravelDestinationLocationId == characterState.CurrentLocationId &&
                characterState.TravelProgress <= 0f)
            {
                return;
            }

            characterState.TravelProgress += step;

            if (characterState.TravelProgress < 1f)
            {
                return;
            }

            characterState.TravelProgress = 0f;
            characterState.CurrentLocationId = characterState.TravelDestinationLocationId;
            characterState.TravelOriginLocationId = characterState.CurrentLocationId;
        }

        public void ResolveTask(RunState runState, CharacterDefinition actor, TaskDefinition task)
        {
            var actorState = runState.GetCharacter(actor.CharacterId);

            if (task == null ||
                task.RequiredLocation == null ||
                actorState.IsTravelling ||
                actorState.CurrentLocationId != task.RequiredLocation.LocationId)
            {
                return;
            }

            foreach (var change in task.ActorResourceChanges)
            {
                actorState.ChangeResource(change.ResourceId, change.Amount);
            }

            actorState.CurrentTaskId = string.Empty;
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
                characterState.CurrentTaskId = string.Empty;
                characterState.TravelProgress = 0f;
                characterState.TravelOriginLocationId = characterState.CurrentLocationId;
                characterState.TravelDestinationLocationId = characterState.CurrentLocationId;
            }

            runState.Calendar.Advance(calendar);
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
                targetState.ChangeResource(resourceChange.ResourceId, resourceChange.Amount);
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
    }
}
