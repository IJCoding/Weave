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
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", new List<ResourceAmount>
            {
                new ResourceAmount { ResourceId = "iron", Amount = 2 }
            });

            var gameObject = new GameObject("Session Under Test");
            try
            {
                var session = gameObject.AddComponent<PrototypeGameSession>();
                session.Configure(calendar, new[] { home, mine }, new[] { miner }, new[] { task });
                session.StartRun(miner);

                Assert.That(session.GetPlayerTasks(), Has.Count.EqualTo(1));

                var command = session.AssignPlayerTask(task);
                Assert.That(command.CharacterId, Is.EqualTo("miner"));
                Assert.That(command.OriginLocationId, Is.EqualTo("home"));
                Assert.That(command.DestinationLocationId, Is.EqualTo("mine"));

                session.TickCharacterTravel("miner", 1f);
                Assert.That(session.RunState.GetCharacter("miner").CurrentLocationId, Is.EqualTo("mine"));

                session.ResolvePlayerTask(task);
                Assert.That(session.RunState.GetCharacter("miner").GetResource("iron"), Is.EqualTo(2));
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
