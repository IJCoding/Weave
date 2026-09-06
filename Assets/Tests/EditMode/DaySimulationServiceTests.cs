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
        public void AdvanceDay_ResetsPerDayTravelAndTaskState()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SetField(calendar, "startingYear", 1);
            SetField(calendar, "seasons", new List<string> { "Spring", "Summer" });
            SetField(calendar, "daysPerSeason", 3);

            var home = ScriptableObject.CreateInstance<LocationDefinition>();
            SetField(home, "locationId", "home");

            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SetField(character, "characterId", "miner");
            SetField(character, "displayName", "Miner");
            SetField(character, "homeLocation", home);
            SetField(character, "startingResources", new List<ResourceAmount>());
            SetField(character, "developerCanon", new List<CanonDecisionDefault>());

            var service = new DaySimulationService(new CanonResolver());
            var runState = service.CreateInitialState(calendar, new[] { home }, new[] { character }, character);
            var characterState = runState.GetCharacter(character.CharacterId);

            characterState.CurrentTaskId = "mine_iron";
            characterState.TravelOriginLocationId = "home";
            characterState.TravelDestinationLocationId = "mine";
            characterState.TravelProgress = 0.5f;

            service.AdvanceDay(runState, calendar);

            Assert.Multiple(() =>
            {
                Assert.That(runState.Calendar.DayOfSeason, Is.EqualTo(2));
                Assert.That(characterState.CurrentTaskId, Is.EqualTo(string.Empty));
                Assert.That(characterState.TravelProgress, Is.EqualTo(0f));
                Assert.That(characterState.TravelOriginLocationId, Is.EqualTo(characterState.CurrentLocationId));
                Assert.That(characterState.TravelDestinationLocationId, Is.EqualTo(characterState.CurrentLocationId));
            });
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field.SetValue(target, value);
        }
    }
}
