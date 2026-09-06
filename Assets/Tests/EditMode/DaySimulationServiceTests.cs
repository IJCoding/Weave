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
            Assert.That(runState.GetCharacter("miner").GetResource("iron"), Is.EqualTo(0));
            AssertReset(runState.GetCharacter("miner"));
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
