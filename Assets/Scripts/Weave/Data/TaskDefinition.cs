using System.Collections.Generic;
using UnityEngine;

namespace Weave.Data
{
    [CreateAssetMenu(menuName = "Weave/Task Definition")]
    public sealed class TaskDefinition : ScriptableObject
    {
        [SerializeField] private string taskId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private LocationDefinition requiredLocation;
        [SerializeField] private List<CharacterDefinition> eligibleCharacters = new List<CharacterDefinition>();
        [SerializeField] private List<string> requiredWorldFlags = new List<string>();
        [SerializeField] private List<string> blockedWorldFlags = new List<string>();
        [SerializeField] private float durationSeconds = 25f;
        [SerializeField] private List<ResourceAmount> actorResourceChanges = new List<ResourceAmount>();
        [SerializeField] private EventDefinition followUpEvent;

        public string TaskId => taskId;
        public string DisplayName => displayName;
        public LocationDefinition RequiredLocation => requiredLocation;
        public IReadOnlyList<CharacterDefinition> EligibleCharacters => eligibleCharacters;
        public IReadOnlyList<string> RequiredWorldFlags => requiredWorldFlags;
        public IReadOnlyList<string> BlockedWorldFlags => blockedWorldFlags;
        public float DurationSeconds => durationSeconds;
        public IReadOnlyList<ResourceAmount> ActorResourceChanges => actorResourceChanges;
        public EventDefinition FollowUpEvent => followUpEvent;

        public bool IsAvailableToAllCharacters => eligibleCharacters.Count == 0;
    }
}
