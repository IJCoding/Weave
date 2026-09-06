using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Weave.Data;
using Weave.Simulation;

namespace Weave.Tests.EditMode
{
    public sealed class PrototypeGameSessionTests
    {
        [Test]
        public void ConfigureAndRun_UsesProvidedDefinitionsForTravelAndTaskResolution()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring" });
            SetField(calendar, "daysPerSeason", 3);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(home, "locationId", "home");
            SetField(home, "displayName", "Home");
            SetField(home, "mapPosition", Vector2.zero);

            var mine = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(mine, "locationId", "mine");
            SetField(mine, "displayName", "Mine");
            SetField(mine, "mapPosition", new Vector2(4f, 0f));

            var miner = CreateCharacter("miner", "Miner", home);
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SetField(task, "taskId", "mine_iron");
            SetField(task, "displayName", "Mine Iron");
            SetField(task, "requiredLocation", mine);
            SetField(task, "eligibleCharacters", new List<CharacterDefinition> { miner });
            SetField(task, "requiredWorldFlags", new List<string>());
            SetField(task, "blockedWorldFlags", new List<string>());
            SetField(task, "actorResourceChanges", new List<ResourceAmount>
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
            SetField(character, "characterId", id);
            SetField(character, "displayName", displayName);
            SetField(character, "homeLocation", home);
            SetField(character, "startingResources", new List<ResourceAmount>());
            SetField(character, "developerCanon", new List<CanonDecisionDefault>());
            return character;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
