using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;
using Weave.Simulation;

namespace Weave.Tests.EditMode
{
    public sealed class DaySimulationServiceTests
    {
        [Test]
        public void AdvanceDay_ResetsPerDayTravelAndTaskStateForAllCharacters()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring", "Summer" });
            SetField(calendar, "daysPerSeason", 3);
            SetField(calendar, "dayDurationSeconds", 300f);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(home, "locationId", "home");

            var miner = CreateCharacter("miner", "Miner", home);
            var lumberjack = CreateCharacter("lumberjack", "Lumberjack", home);

            var service = new DaySimulationService(new CanonResolver());
            var runState = service.CreateInitialState(calendar, new[] { home }, new[] { miner, lumberjack }, miner);
            var minerState = runState.GetCharacter(miner.CharacterId);
            var lumberjackState = runState.GetCharacter(lumberjack.CharacterId);

            minerState.CurrentTaskId = "mine_iron";
            minerState.TravelOriginLocationId = "home";
            minerState.TravelDestinationLocationId = "mine";
            minerState.TravelProgress = 0.5f;
            minerState.CurrentTaskPhase = TaskPhase.Travelling;
            minerState.TravelDurationSeconds = 10f;
            minerState.TaskDurationSeconds = 25f;
            minerState.TaskElapsedSeconds = 5f;

            lumberjackState.CurrentTaskId = "harvest_timber";
            lumberjackState.TravelOriginLocationId = "home";
            lumberjackState.TravelDestinationLocationId = "forest";
            lumberjackState.TravelProgress = 0.75f;
            lumberjackState.CurrentTaskPhase = TaskPhase.Working;
            lumberjackState.TravelDurationSeconds = 10f;
            lumberjackState.TaskDurationSeconds = 25f;
            lumberjackState.TaskElapsedSeconds = 12f;

            service.AdvanceDay(runState, calendar);

            Assert.That(runState.Calendar.DayOfSeason, Is.EqualTo(2));
            Assert.That(runState.DayTimer.RemainingSeconds, Is.EqualTo(300f));
            AssertReset(minerState);
            AssertReset(lumberjackState);
        }

        [Test]
        public void AdvanceSimulation_InterruptsWorkingTaskAtDayEnd()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring" });
            SetField(calendar, "daysPerSeason", 3);
            SetField(calendar, "dayDurationSeconds", 5f);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(home, "locationId", "home");

            var mine = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(mine, "locationId", "mine");

            var miner = CreateCharacter("miner", "Miner", home);
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SetField(task, "taskId", "mine_iron");
            SetField(task, "displayName", "Mine Iron");
            SetField(task, "requiredLocation", mine);
            SetField(task, "eligibleCharacters", new List<CharacterDefinition> { miner });
            SetField(task, "requiredWorldFlags", new List<string>());
            SetField(task, "blockedWorldFlags", new List<string>());
            SetField(task, "durationSeconds", 8f);
            SetField(task, "actorResourceChanges", new List<ResourceAmount>
            {
                new ResourceAmount { ResourceId = "iron", Amount = 2 }
            });

            var service = new DaySimulationService(new CanonResolver());
            var runState = service.CreateInitialState(calendar, new[] { home, mine }, new[] { miner }, miner);
            service.StartTravel(runState, miner, task, 1f);
            service.AdvanceSimulation(runState, calendar, miner, new[] { task }, 1f);

            var result = service.AdvanceSimulation(runState, calendar, miner, new[] { task }, 5f);

            Assert.That(result.DayAdvanced, Is.True);
            Assert.That(result.TaskInterrupted, Is.True);
            Assert.That(result.InterruptedTaskId, Is.EqualTo("mine_iron"));
            Assert.That(runState.Calendar.DayOfSeason, Is.EqualTo(2));
            Assert.That(runState.GetCharacter("miner").GetStoredResource("iron"), Is.EqualTo(0));
            AssertReset(runState.GetCharacter("miner"));
        }

        [Test]
        public void AdvanceSimulation_SameLocationPreparationTransitionsToWorkingAndCompletes()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring" });
            SetField(calendar, "daysPerSeason", 3);
            SetField(calendar, "dayDurationSeconds", 300f);

            var mine = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(mine, "locationId", "mine");
            SetField(mine, "mapPosition", new Vector2(0f, 3f));

            var miner = CreateCharacter("miner", "Miner", mine);
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SetField(task, "taskId", "mine_iron");
            SetField(task, "displayName", "Mine Iron");
            SetField(task, "requiredLocation", mine);
            SetField(task, "eligibleCharacters", new List<CharacterDefinition> { miner });
            SetField(task, "requiredWorldFlags", new List<string>());
            SetField(task, "blockedWorldFlags", new List<string>());
            SetField(task, "durationSeconds", 2f);
            SetField(task, "actorResourceChanges", new List<ResourceAmount>
            {
                new ResourceAmount { ResourceId = "iron", Amount = 1 }
            });

            var service = new DaySimulationService(new CanonResolver());
            var runState = service.CreateInitialState(calendar, new[] { mine }, new[] { miner }, miner);
            service.StartTravel(runState, miner, task, 1f);

            var prepStep = service.AdvanceSimulation(runState, calendar, miner, new[] { task }, 1f);
            Assert.That(prepStep.StateChanged, Is.True);
            Assert.That(runState.GetCharacter("miner").CurrentTaskPhase, Is.EqualTo(TaskPhase.Working));
            Assert.That(runState.GetCharacter("miner").CurrentLocationId, Is.EqualTo("mine"));

            var finishStep = service.AdvanceSimulation(runState, calendar, miner, new[] { task }, 2f);
            Assert.That(finishStep.TaskCompleted, Is.True);
            Assert.That(runState.GetCharacter("miner").GetStoredResource("iron"), Is.EqualTo(1));
            Assert.That(runState.GetCharacter("miner").CurrentTaskPhase, Is.EqualTo(TaskPhase.None));
        }

        [Test]
        public void AdvanceSimulation_ReturnHomeDepositsCarriedResourcesAndAppliesCarryPenalty()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring" });
            SetField(calendar, "daysPerSeason", 3);
            SetField(calendar, "dayDurationSeconds", 300f);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(home, "locationId", "home");
            SetField(home, "mapPosition", new Vector2(0f, 0f));

            var mine = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(mine, "locationId", "mine");
            SetField(mine, "mapPosition", new Vector2(0f, 3f));

            var miner = CreateCharacter("miner", "Miner", home);
            var mineTask = ScriptableObject.CreateInstance<TaskDefinition>();
            SetField(mineTask, "taskId", "mine_iron");
            SetField(mineTask, "displayName", "Mine Iron");
            SetField(mineTask, "requiredLocation", mine);
            SetField(mineTask, "eligibleCharacters", new List<CharacterDefinition> { miner });
            SetField(mineTask, "requiredWorldFlags", new List<string>());
            SetField(mineTask, "blockedWorldFlags", new List<string>());
            SetField(mineTask, "durationSeconds", 1f);
            SetField(mineTask, "actorResourceChanges", new List<ResourceAmount> { new ResourceAmount { ResourceId = "iron", Amount = 1 } });
            SetField(mineTask, "rewardsAddedToCarriedResources", true);

            var returnHomeTask = ScriptableObject.CreateInstance<TaskDefinition>();
            SetField(returnHomeTask, "taskId", "return_home");
            SetField(returnHomeTask, "displayName", "Return Home");
            SetField(returnHomeTask, "requiredLocation", home);
            SetField(returnHomeTask, "eligibleCharacters", new List<CharacterDefinition>());
            SetField(returnHomeTask, "requiredWorldFlags", new List<string>());
            SetField(returnHomeTask, "blockedWorldFlags", new List<string>());
            SetField(returnHomeTask, "durationSeconds", 0f);
            SetField(returnHomeTask, "actorResourceChanges", new List<ResourceAmount>());
            SetField(returnHomeTask, "completeOnArrival", true);
            SetField(returnHomeTask, "unavailableWhenAlreadyAtRequiredLocation", true);

            var iron = ScriptableObject.CreateInstance<ResourceDefinition>();
            SetField(iron, "resourceId", "iron");
            SetField(iron, "carryWeightPerUnit", 2f);

            var service = new DaySimulationService(new CanonResolver());
            var runState = service.CreateInitialState(calendar, new[] { home, mine }, new[] { miner }, miner);

            service.StartTravel(runState, miner, mineTask, 1f);
            service.AdvanceSimulation(runState, calendar, miner, new[] { mineTask, returnHomeTask }, 2f);
            Assert.That(runState.GetCharacter("miner").GetCarriedResource("iron"), Is.EqualTo(1));

            var returnDuration = service.EstimateTravelDuration(
                runState,
                miner,
                returnHomeTask,
                new[] { home, mine },
                new[] { iron },
                2f,
                1f,
                0.05f);
            Assert.That(returnDuration, Is.EqualTo(6.6f).Within(0.001f));

            service.StartTravel(runState, miner, returnHomeTask, returnDuration);
            var result = service.AdvanceSimulation(runState, calendar, miner, new[] { mineTask, returnHomeTask }, returnDuration);

            Assert.That(result.TaskCompleted, Is.True);
            Assert.That(runState.GetCharacter("miner").CurrentLocationId, Is.EqualTo("home"));
            Assert.That(runState.GetCharacter("miner").GetCarriedResource("iron"), Is.EqualTo(0));
            Assert.That(runState.GetCharacter("miner").GetStoredResource("iron"), Is.EqualTo(1));
            Assert.That(runState.GetCharacter("miner").CurrentTaskPhase, Is.EqualTo(TaskPhase.None));
        }

        private static CharacterDefinition CreateCharacter(string id, string displayName, LocationDefinition home)
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SetField(character, "characterId", id);
            SetField(character, "displayName", displayName);
            SetField(character, "homeLocation", home);
            SetField(character, "startingResources", new List<ResourceAmount>());
            SetField(character, "developerCanon", new List<CanonDecisionDefault>());
            return character;
        }

        private static void AssertReset(Weave.Runtime.CharacterState characterState)
        {
            Assert.That(characterState.CurrentTaskId, Is.EqualTo(string.Empty));
            Assert.That(characterState.CurrentTaskPhase, Is.EqualTo(TaskPhase.None));
            Assert.That(characterState.TravelProgress, Is.EqualTo(0f));
            Assert.That(characterState.TravelOriginLocationId, Is.EqualTo(characterState.CurrentLocationId));
            Assert.That(characterState.TravelDestinationLocationId, Is.EqualTo(characterState.CurrentLocationId));
            Assert.That(characterState.TravelDurationSeconds, Is.EqualTo(0f));
            Assert.That(characterState.TaskElapsedSeconds, Is.EqualTo(0f));
            Assert.That(characterState.TaskDurationSeconds, Is.EqualTo(0f));
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
