using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Weave.Data;
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

            lumberjackState.CurrentTaskId = "harvest_timber";
            lumberjackState.TravelOriginLocationId = "home";
            lumberjackState.TravelDestinationLocationId = "forest";
            lumberjackState.TravelProgress = 0.75f;

            service.AdvanceDay(runState, calendar);

            Assert.That(runState.Calendar.DayOfSeason, Is.EqualTo(2));
            AssertReset(minerState);
            AssertReset(lumberjackState);
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
            Assert.That(characterState.TravelProgress, Is.EqualTo(0f));
            Assert.That(characterState.TravelOriginLocationId, Is.EqualTo(characterState.CurrentLocationId));
            Assert.That(characterState.TravelDestinationLocationId, Is.EqualTo(characterState.CurrentLocationId));
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
