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
        [NonSerialized] public HashSet<string> Flags = new HashSet<string>();

        public LocationState(string locationId)
        {
            LocationId = locationId;
        }
    }

    [Serializable]
    public sealed class TravelRouteState
    {
        [NonSerialized] public List<Vector2> Waypoints = new List<Vector2>();
        [NonSerialized] public List<float> CumulativeDurations = new List<float>();
        [NonSerialized] public float TotalDurationSeconds;

        public void Set(IReadOnlyList<Vector2> waypoints, IReadOnlyList<float> cumulativeDurations, float totalDurationSeconds)
        {
            Waypoints.Clear();
            CumulativeDurations.Clear();

            if (waypoints != null)
            {
                Waypoints.AddRange(waypoints);
            }

            if (cumulativeDurations != null)
            {
                CumulativeDurations.AddRange(cumulativeDurations);
            }

            TotalDurationSeconds = Mathf.Max(totalDurationSeconds, 0f);
        }

        public void Clear()
        {
            Waypoints.Clear();
            CumulativeDurations.Clear();
            TotalDurationSeconds = 0f;
        }

        public Vector2 Evaluate(float normalizedProgress)
        {
            if (Waypoints.Count == 0)
            {
                return Vector2.zero;
            }

            if (Waypoints.Count == 1 || TotalDurationSeconds <= Mathf.Epsilon)
            {
                return Waypoints[Waypoints.Count - 1];
            }

            var elapsed = Mathf.Clamp01(normalizedProgress) * TotalDurationSeconds;

            for (var index = 1; index < Waypoints.Count; index++)
            {
                var previousTime = index - 1 < CumulativeDurations.Count ? CumulativeDurations[index - 1] : 0f;
                var currentTime = index < CumulativeDurations.Count ? CumulativeDurations[index] : TotalDurationSeconds;

                if (elapsed > currentTime && index < Waypoints.Count - 1)
                {
                    continue;
                }

                var segmentDuration = Mathf.Max(currentTime - previousTime, 0.0001f);
                var segmentProgress = Mathf.Clamp01((elapsed - previousTime) / segmentDuration);
                return Vector2.Lerp(Waypoints[index - 1], Waypoints[index], segmentProgress);
            }

            return Waypoints[Waypoints.Count - 1];
        }
    }

    [Serializable]
    public sealed class CharacterState
    {
        public string CharacterId;
        public string HomeLocationId;
        public string CurrentLocationId;
        public string TravelOriginLocationId;
        public string TravelDestinationLocationId;
        public float TravelProgress;
        public string CurrentTaskId;
        public TaskPhase CurrentTaskPhase;
        public float TravelDurationSeconds;
        public float TaskElapsedSeconds;
        public float TaskDurationSeconds;
        public bool CompleteTaskOnArrival;
        [NonSerialized] public Dictionary<string, int> StoredResources = new Dictionary<string, int>();
        [NonSerialized] public Dictionary<string, int> CarriedResources = new Dictionary<string, int>();
        [NonSerialized] public TravelRouteState TravelRoute = new TravelRouteState();

        public CharacterState(CharacterDefinition definition)
        {
            CharacterId = definition.CharacterId;
            HomeLocationId = definition.HomeLocationId;
            CurrentLocationId = HomeLocationId;
            TravelOriginLocationId = CurrentLocationId;
            TravelDestinationLocationId = string.Empty;
            CurrentTaskPhase = TaskPhase.None;

            foreach (var resource in definition.StartingResources)
            {
                StoredResources[resource.ResourceId] = resource.Amount;
            }
        }

        public bool HasActiveTask => !string.IsNullOrEmpty(CurrentTaskId);
        public bool IsTravelling => CurrentTaskPhase == TaskPhase.Travelling;
        public bool IsWorkingOnTask => CurrentTaskPhase == TaskPhase.Working && HasActiveTask;

        public int GetStoredResource(string resourceId)
        {
            return StoredResources.TryGetValue(resourceId, out var amount) ? amount : 0;
        }

        public int GetCarriedResource(string resourceId)
        {
            return CarriedResources.TryGetValue(resourceId, out var amount) ? amount : 0;
        }

        public void ChangeStoredResource(string resourceId, int delta)
        {
            StoredResources[resourceId] = GetStoredResource(resourceId) + delta;
        }

        public void ChangeCarriedResource(string resourceId, int delta)
        {
            CarriedResources[resourceId] = GetCarriedResource(resourceId) + delta;
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
        [NonSerialized] public Dictionary<string, CharacterState> Characters = new Dictionary<string, CharacterState>();
        [NonSerialized] public Dictionary<string, LocationState> Locations = new Dictionary<string, LocationState>();
        [NonSerialized] public HashSet<string> WorldFlags = new HashSet<string>();

        public CharacterState GetCharacter(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
            {
                throw new ArgumentException("Character ID must be non-empty when resolving run-state characters.", nameof(characterId));
            }

            if (!Characters.TryGetValue(characterId, out var characterState) || characterState == null)
            {
                throw new KeyNotFoundException($"RunState does not contain a character with ID '{characterId}'.");
            }

            return characterState;
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
