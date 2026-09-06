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

        public int StartingYear => startingYear;
        public IReadOnlyList<string> Seasons => seasons;
        public int DaysPerSeason => daysPerSeason;
        public int TotalDaysPerYear => seasons.Count * daysPerSeason;
    }
}
