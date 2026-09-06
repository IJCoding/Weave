using System.Collections.Generic;
using UnityEngine;

namespace Weave.Data
{
    [CreateAssetMenu(menuName = "Weave/Calendar Definition")]
    public sealed class GameCalendarDefinition : ScriptableObject
    {
        [SerializeField] private int startingYear = 1;
        [SerializeField] private List<string> seasons = new List<string> { "Spring", "Summer", "Autumn", "Winter" };
        [SerializeField] private int daysPerSeason = 7;
        [SerializeField] private float dayDurationSeconds = 300f;
        [SerializeField] private float normalSimulationSpeed = 1f;
        [SerializeField] private float fastForwardSimulationSpeed = 3f;

        public int StartingYear => startingYear;
        public IReadOnlyList<string> Seasons => seasons;
        public int DaysPerSeason => daysPerSeason;
        public int TotalDaysPerYear => seasons.Count * daysPerSeason;
        public float DayDurationSeconds => dayDurationSeconds;
        public float NormalSimulationSpeed => normalSimulationSpeed;
        public float FastForwardSimulationSpeed => fastForwardSimulationSpeed;
    }
}
