using UnityEngine;

namespace Weave.Simulation
{
    public enum SimulationLogCategory
    {
        Travel,
        Work,
        Resource,
        Event,
        Decision,
        System
    }

    public readonly struct SimulationLogSignal
    {
        public SimulationLogSignal(SimulationLogCategory category, string characterId, string message)
        {
            Category = category;
            CharacterId = characterId;
            Message = message ?? string.Empty;
        }

        public SimulationLogCategory Category { get; }
        public string CharacterId { get; }
        public string Message { get; }
    }

    public readonly struct SimulationLogEntry
    {
        public SimulationLogEntry(
            int dayOfSeason,
            float dayElapsedSeconds,
            SimulationLogCategory category,
            string characterId,
            string message)
        {
            DayOfSeason = Mathf.Max(dayOfSeason, 1);
            DayElapsedSeconds = Mathf.Max(0f, dayElapsedSeconds);
            Category = category;
            CharacterId = characterId ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public int DayOfSeason { get; }
        public float DayElapsedSeconds { get; }
        public SimulationLogCategory Category { get; }
        public string CharacterId { get; }
        public string Message { get; }
    }
}
