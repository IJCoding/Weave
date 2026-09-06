using System;
using System.Collections.Generic;
using UnityEngine;
using Weave.Data;

namespace Weave.Runtime
{
    public enum TaskPhase
    {
        None,
        Travelling,
        Working
    }

    [Serializable]
    public sealed class CalendarState
    {
        public int Year;
        public int SeasonIndex;
        public int DayOfSeason;

        public CalendarState(int startingYear)
        {
            Year = startingYear;
            SeasonIndex = 0;
            DayOfSeason = 1;
        }

        public void Advance(GameCalendarDefinition definition)
        {
            DayOfSeason++;

            if (DayOfSeason <= definition.DaysPerSeason)
            {
                return;
            }

            DayOfSeason = 1;
            SeasonIndex++;

            if (SeasonIndex < definition.Seasons.Count)
            {
                return;
            }

            SeasonIndex = 0;
            Year++;
        }
    }

    [Serializable]
    public sealed class DayTimerState
    {
        public float DurationSeconds;
        public float RemainingSeconds;

        public DayTimerState(float durationSeconds)
        {
            Reset(durationSeconds);
        }

        public void Reset(float durationSeconds)
        {
            DurationSeconds = durationSeconds > 0f ? durationSeconds : 1f;
            RemainingSeconds = DurationSeconds;
        }
    }

    [Serializable]
    public sealed class LocationState
    {
        public string LocationId;
        public HashSet<string> Flags = new HashSet<string>();

        public LocationState(string locationId)
        {
            LocationId = locationId;
        }
    }

    [Serializable]
    public sealed class CharacterState
    {
        public string CharacterId;
        public string CurrentLocationId;
        public string TravelOriginLocationId;
        public string TravelDestinationLocationId;
        public float TravelProgress;
        public string CurrentTaskId;
        public TaskPhase CurrentTaskPhase;
        public float TravelDurationSeconds;
        public float TaskElapsedSeconds;
        public float TaskDurationSeconds;
        public Dictionary<string, int> Resources = new Dictionary<string, int>();

        public CharacterState(CharacterDefinition definition)
        {
            CharacterId = definition.CharacterId;
            CurrentLocationId = definition.HomeLocation != null ? definition.HomeLocation.LocationId : string.Empty;
            TravelOriginLocationId = CurrentLocationId;
            TravelDestinationLocationId = CurrentLocationId;
            CurrentTaskPhase = TaskPhase.None;

            foreach (var resource in definition.StartingResources)
            {
                Resources[resource.ResourceId] = resource.Amount;
            }
        }

        public bool HasActiveTask => !string.IsNullOrEmpty(CurrentTaskId);
        public bool IsTravelling => CurrentTaskPhase == TaskPhase.Travelling && TravelDestinationLocationId != CurrentLocationId;
        public bool IsWorkingOnTask => CurrentTaskPhase == TaskPhase.Working && HasActiveTask;

        public int GetResource(string resourceId)
        {
            return Resources.TryGetValue(resourceId, out var amount) ? amount : 0;
        }

        public void ChangeResource(string resourceId, int delta)
        {
            Resources[resourceId] = GetResource(resourceId) + delta;
        }
    }

    [Serializable]
    public sealed class PlayerCanonDecisionRecord
    {
        public string CharacterId;
        public string DecisionKey;
        public string OptionId;
    }

    [Serializable]
    public sealed class PlayerCanonState
        : ISerializationCallbackReceiver
    {
        [SerializeField] private List<PlayerCanonDecisionRecord> serializedDecisions =
            new List<PlayerCanonDecisionRecord>();

        private readonly Dictionary<string, Dictionary<string, string>> decisionsByCharacter =
            new Dictionary<string, Dictionary<string, string>>();

        public bool TryGetOption(string characterId, string decisionKey, out string optionId)
        {
            optionId = string.Empty;

            if (!decisionsByCharacter.TryGetValue(characterId, out var characterDecisions))
            {
                return false;
            }

            return characterDecisions.TryGetValue(decisionKey, out optionId);
        }

        public void SetOption(string characterId, string decisionKey, string optionId)
        {
            if (!decisionsByCharacter.TryGetValue(characterId, out var characterDecisions))
            {
                characterDecisions = new Dictionary<string, string>();
                decisionsByCharacter[characterId] = characterDecisions;
            }

            characterDecisions[decisionKey] = optionId;
        }

        public void OnBeforeSerialize()
        {
            serializedDecisions.Clear();

            foreach (var characterEntry in decisionsByCharacter)
            {
                foreach (var decisionEntry in characterEntry.Value)
                {
                    serializedDecisions.Add(new PlayerCanonDecisionRecord
                    {
                        CharacterId = characterEntry.Key,
                        DecisionKey = decisionEntry.Key,
                        OptionId = decisionEntry.Value
                    });
                }
            }
        }

        public void OnAfterDeserialize()
        {
            decisionsByCharacter.Clear();

            foreach (var record in serializedDecisions)
            {
                if (string.IsNullOrEmpty(record.CharacterId) || string.IsNullOrEmpty(record.DecisionKey))
                {
                    continue;
                }

                if (!decisionsByCharacter.TryGetValue(record.CharacterId, out var characterDecisions))
                {
                    characterDecisions = new Dictionary<string, string>();
                    decisionsByCharacter[record.CharacterId] = characterDecisions;
                }

                characterDecisions[record.DecisionKey] = record.OptionId;
            }
        }
    }

    [Serializable]
    public sealed class RunState
    {
        public string ControlledCharacterId;
        public CalendarState Calendar;
        public DayTimerState DayTimer;
        public Dictionary<string, CharacterState> Characters = new Dictionary<string, CharacterState>();
        public Dictionary<string, LocationState> Locations = new Dictionary<string, LocationState>();
        public HashSet<string> WorldFlags = new HashSet<string>();

        public CharacterState GetCharacter(string characterId)
        {
            return Characters[characterId];
        }

        public bool HasWorldFlag(string flagId)
        {
            return WorldFlags.Contains(flagId);
        }

        public void SetWorldFlag(string flagId, bool present)
        {
            if (present)
            {
                WorldFlags.Add(flagId);
            }
            else
            {
                WorldFlags.Remove(flagId);
            }
        }
    }
}
