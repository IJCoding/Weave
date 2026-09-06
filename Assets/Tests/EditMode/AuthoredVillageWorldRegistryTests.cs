using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;
using Weave.Simulation;
using Weave.World;

namespace Weave.Tests.EditMode
{
    public sealed class AuthoredVillageWorldRegistryTests
    {
        [Test]
        public void BuildTravelPlan_PrefersRoadTimeOverShorterOffRoadDistance()
        {
            var root = new GameObject("World");
            try
            {
                var registry = root.AddComponent<AuthoredVillageWorldRegistry>();
                var home = CreateLocation(root.transform, "home", Vector2.zero);
                var mine = CreateLocation(root.transform, "mine", new Vector2(2f, 2f));
                CreateRoad(root.transform, new Vector2(0f, 1f));
                CreateRoad(root.transform, new Vector2(0f, 2f));
                CreateRoad(root.transform, new Vector2(1f, 2f));
                CreateRoad(root.transform, new Vector2(2f, 2f));

                registry.RefreshWorld();
                var plan = registry.BuildTravelPlan("home", "mine", 1f, 1f, 0.1f);

                Assert.That(plan.TotalDurationSeconds, Is.LessThan(Vector2.Distance(Vector2.zero, new Vector2(2f, 2f))));
                Assert.That(plan.Waypoints.Count, Is.GreaterThan(2));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BuildNpcInteractionTasks_OnlyReturnsTalkActionForNpcAtCurrentLocation()
        {
            var root = new GameObject("World");
            try
            {
                var registry = root.AddComponent<AuthoredVillageWorldRegistry>();
                var home = CreateLocation(root.transform, "home", Vector2.zero);
                var mine = CreateLocation(root.transform, "mine", new Vector2(2f, 0f));
                CreateNpc(root.transform, "mina", home, home);
                CreateNpc(root.transform, "rowan", mine, mine);
                registry.RefreshWorld();

                var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
                SerializedFieldUtility.SetPrivateField(calendar, "startingYear", 1);
                SerializedFieldUtility.SetPrivateField(calendar, "seasons", new List<string> { "Spring" });
                SerializedFieldUtility.SetPrivateField(calendar, "daysPerSeason", 5);
                SerializedFieldUtility.SetPrivateField(calendar, "dayDurationSeconds", 300f);
                var runState = new DaySimulationService(new CanonResolver()).CreateInitialState(
                    calendar,
                    registry.BuildRuntimeLocations(),
                    registry.BuildCharacters(CreateCharacter("player", "home")),
                    CreateCharacter("player", "home"));
                registry.ApplyInitialCharacterPlacements(runState, CreateCharacter("player", "home"));

                var talkTasks = registry.BuildNpcInteractionTasks("home", runState);

                Assert.That(talkTasks, Has.Count.EqualTo(1));
                Assert.That(talkTasks[0].DisplayName, Is.EqualTo("Talk to Mina"));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static AuthoredVillageLocation CreateLocation(Transform parent, string id, Vector2 position)
        {
            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            var location = gameObject.AddComponent<AuthoredVillageLocation>();
            SerializedFieldUtility.SetPrivateField(location, "locationId", id);
            SerializedFieldUtility.SetPrivateField(location, "displayName", id);
            return location;
        }

        private static void CreateRoad(Transform parent, Vector2 position)
        {
            var gameObject = new GameObject("Road");
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            gameObject.AddComponent<AuthoredVillageRoadTile>();
        }

        private static AuthoredVillageNpc CreateNpc(Transform parent, string id, AuthoredVillageLocation starting, AuthoredVillageLocation home)
        {
            var gameObject = new GameObject(id);
            gameObject.transform.SetParent(parent, false);
            var npc = gameObject.AddComponent<AuthoredVillageNpc>();
            SerializedFieldUtility.SetPrivateField(npc, "characterDefinition", CreateCharacter(id, home.LocationId));
            SerializedFieldUtility.SetPrivateField(npc, "startingLocation", starting);
            SerializedFieldUtility.SetPrivateField(npc, "homeLocation", home);
            SerializedFieldUtility.SetPrivateField(npc, "talkEvent", CreateEvent(id));
            return npc;
        }

        private static CharacterDefinition CreateCharacter(string id, string homeLocationId)
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SerializedFieldUtility.SetPrivateField(character, "characterId", id);
            SerializedFieldUtility.SetPrivateField(character, "displayName", char.ToUpperInvariant(id[0]) + id.Substring(1));
            SerializedFieldUtility.SetPrivateField(character, "homeLocationId", homeLocationId);
            SerializedFieldUtility.SetPrivateField(character, "startingResources", new List<ResourceAmount>());
            SerializedFieldUtility.SetPrivateField(character, "developerCanon", new List<CanonDecisionDefault>());
            return character;
        }

        private static EventDefinition CreateEvent(string id)
        {
            var eventDefinition = ScriptableObject.CreateInstance<EventDefinition>();
            SerializedFieldUtility.SetPrivateField(eventDefinition, "eventId", $"talk_{id}");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "title", $"Talk to {id}");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "prompt", "Hello there.");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "sourceLabel", id);
            SerializedFieldUtility.SetPrivateField(eventDefinition, "triggerConditions", new List<WorldFlagRequirement>());
            SerializedFieldUtility.SetPrivateField(
                eventDefinition,
                "options",
                new List<DecisionOptionDefinition>
                {
                    CreateOption("continue", "Continue")
                });
            return eventDefinition;
        }

        private static DecisionOptionDefinition CreateOption(string optionId, string label)
        {
            var option = new DecisionOptionDefinition();
            SerializedFieldUtility.SetPrivateField(option, "optionId", optionId);
            SerializedFieldUtility.SetPrivateField(option, "label", label);
            SerializedFieldUtility.SetPrivateField(option, "outcomes", new List<OutcomeVariantDefinition>());
            return option;
        }
    }
}
