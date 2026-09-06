using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;
using Weave.Simulation;

namespace Weave.Tests.EditMode
{
    public sealed class PrototypeGameSessionTests
    {
        [Test]
        public void ConfigureAndRun_UsesProvidedDefinitionsForTravelAndTaskResolution()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SerializedFieldUtility.SetPrivateField(calendar, "startingYear", 1);
            SerializedFieldUtility.SetPrivateField(calendar, "seasons", new List<string> { "Spring" });
            SerializedFieldUtility.SetPrivateField(calendar, "daysPerSeason", 3);
            SerializedFieldUtility.SetPrivateField(calendar, "dayDurationSeconds", 300f);
            SerializedFieldUtility.SetPrivateField(calendar, "normalSimulationSpeed", 1f);
            SerializedFieldUtility.SetPrivateField(calendar, "fastForwardSimulationSpeed", 3f);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SerializedFieldUtility.SetPrivateField(home, "locationId", "home");
            SerializedFieldUtility.SetPrivateField(home, "displayName", "Home");
            SerializedFieldUtility.SetPrivateField(home, "mapPosition", Vector2.zero);

            var mine = ScriptableObject.CreateInstance<LocationDefinition>();
            SerializedFieldUtility.SetPrivateField(mine, "locationId", "mine");
            SerializedFieldUtility.SetPrivateField(mine, "displayName", "Mine");
            SerializedFieldUtility.SetPrivateField(mine, "mapPosition", new Vector2(4f, 0f));

            var miner = CreateCharacter("miner", "Miner", home);
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SerializedFieldUtility.SetPrivateField(task, "taskId", "mine_iron");
            SerializedFieldUtility.SetPrivateField(task, "displayName", "Mine Iron");
            SerializedFieldUtility.SetPrivateField(task, "requiredLocation", mine);
            SerializedFieldUtility.SetPrivateField(task, "eligibleCharacters", new List<CharacterDefinition> { miner });
            SerializedFieldUtility.SetPrivateField(task, "requiredWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "blockedWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "durationSeconds", 5f);
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", new List<ResourceAmount>
            {
                new ResourceAmount { ResourceId = "iron", Amount = 2 }
            });
            SerializedFieldUtility.SetPrivateField(task, "rewardsAddedToCarriedResources", true);

            var iron = ScriptableObject.CreateInstance<ResourceDefinition>();
            SerializedFieldUtility.SetPrivateField(iron, "resourceId", "iron");
            SerializedFieldUtility.SetPrivateField(iron, "displayName", "Iron Ore");
            SerializedFieldUtility.SetPrivateField(iron, "carryWeightPerUnit", 2f);

            var gameObject = new GameObject("Session Under Test");
            try
            {
                var session = gameObject.AddComponent<PrototypeGameSession>();
                SerializedFieldUtility.SetPrivateField(session, "secondsPerDistanceUnit", 0.25f);
                SerializedFieldUtility.SetPrivateField(session, "sameLocationPreparationSeconds", 1f);
                session.Configure(calendar, new[] { home, mine }, new[] { miner }, new[] { task }, new[] { iron });
                session.StartRun(miner);

                Assert.That(session.GetPlayerTasks(), Has.Count.EqualTo(1));

                var command = session.AssignPlayerTask(task);
                Assert.That(command.CharacterId, Is.EqualTo("miner"));
                Assert.That(command.OriginLocationId, Is.EqualTo("home"));
                Assert.That(command.DestinationLocationId, Is.EqualTo("mine"));

                session.AdvanceSimulation(1f);
                Assert.That(session.RunState.GetCharacter("miner").CurrentLocationId, Is.EqualTo("mine"));
                Assert.That(session.RunState.GetCharacter("miner").CurrentTaskId, Is.EqualTo("mine_iron"));
                Assert.That(session.RunState.GetCharacter("miner").TaskElapsedSeconds, Is.EqualTo(0f));
                Assert.That(session.RunState.GetCharacter("miner").GetCarriedResource("iron"), Is.EqualTo(0));

                session.AdvanceSimulation(4f);
                Assert.That(session.RunState.GetCharacter("miner").GetCarriedResource("iron"), Is.EqualTo(0));

                session.AdvanceSimulation(1f);
                Assert.That(session.RunState.GetCharacter("miner").GetCarriedResource("iron"), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }

        }

        [Test]
            public void AdvanceSimulation_DoesNotProgressWhilePaused()
            {
                var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
                SerializedFieldUtility.SetPrivateField(calendar, "startingYear", 1);
                SerializedFieldUtility.SetPrivateField(calendar, "seasons", new List<string> { "Spring" });
                SerializedFieldUtility.SetPrivateField(calendar, "daysPerSeason", 3);
                SerializedFieldUtility.SetPrivateField(calendar, "dayDurationSeconds", 300f);
                SerializedFieldUtility.SetPrivateField(calendar, "normalSimulationSpeed", 1f);
                SerializedFieldUtility.SetPrivateField(calendar, "fastForwardSimulationSpeed", 3f);

                var home = ScriptableObject.CreateInstance<LocationDefinition>();
                SerializedFieldUtility.SetPrivateField(home, "locationId", "home");
                SerializedFieldUtility.SetPrivateField(home, "displayName", "Home");

                var miner = CreateCharacter("miner", "Miner", home);

                var gameObject = new GameObject("Paused Session Under Test");
                try
                {
                    var session = gameObject.AddComponent<PrototypeGameSession>();
                    session.Configure(calendar, new[] { home }, new[] { miner }, new TaskDefinition[0], new ResourceDefinition[0]);
                    session.StartRun(miner);
                    session.SetSimulationSpeed(SimulationSpeedMode.FastForward);
                    session.PushPauseOverride();
                    session.AdvanceSimulation(10f);

                    Assert.That(session.RunState.DayTimer.RemainingSeconds, Is.EqualTo(300f));

                    session.PopPauseOverride();
                    session.AdvanceSimulation(10f);
                    Assert.That(session.RunState.DayTimer.RemainingSeconds, Is.EqualTo(270f));
                }
                finally
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

        private static CharacterDefinition CreateCharacter(string id, string displayName, LocationDefinition home)
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SerializedFieldUtility.SetPrivateField(character, "characterId", id);
            SerializedFieldUtility.SetPrivateField(character, "displayName", displayName);
            SerializedFieldUtility.SetPrivateField(character, "homeLocation", home);
            SerializedFieldUtility.SetPrivateField(character, "startingResources", new List<ResourceAmount>());
            SerializedFieldUtility.SetPrivateField(character, "developerCanon", new List<CanonDecisionDefault>());
            return character;
        }
    }
}
