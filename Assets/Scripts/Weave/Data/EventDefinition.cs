using System;
using System.Collections.Generic;
using UnityEngine;

namespace Weave.Data
{
    [Serializable]
    public struct WorldFlagRequirement
    {
        public string FlagId;
        public bool MustBePresent;
    }

    [Serializable]
    public struct WorldFlagMutation
    {
        public string FlagId;
        public bool SetPresent;
    }

    [Serializable]
    public struct CharacterResourceDelta
    {
        public CharacterDefinition Character;
        public string ResourceId;
        public int Amount;
    }

    [Serializable]
    public sealed class OutcomeVariantDefinition
    {
        [SerializeField] private string summaryText = string.Empty;
        [SerializeField] private List<WorldFlagRequirement> conditions = new List<WorldFlagRequirement>();
        [SerializeField] private List<WorldFlagMutation> worldFlagMutations = new List<WorldFlagMutation>();
        [SerializeField] private List<CharacterResourceDelta> resourceChanges = new List<CharacterResourceDelta>();

        public string SummaryText => summaryText;
        public IReadOnlyList<WorldFlagRequirement> Conditions => conditions;
        public IReadOnlyList<WorldFlagMutation> WorldFlagMutations => worldFlagMutations;
        public IReadOnlyList<CharacterResourceDelta> ResourceChanges => resourceChanges;
    }

    [Serializable]
    public sealed class DecisionOptionDefinition
    {
        [SerializeField] private string optionId = string.Empty;
        [SerializeField] private string label = string.Empty;
        [SerializeField] private List<OutcomeVariantDefinition> outcomes = new List<OutcomeVariantDefinition>();

        public string OptionId => optionId;
        public string Label => label;
        public IReadOnlyList<OutcomeVariantDefinition> Outcomes => outcomes;
    }

    [CreateAssetMenu(menuName = "Weave/Event Definition")]
    public sealed class EventDefinition : ScriptableObject
    {
        [SerializeField] private string eventId = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField] private string prompt = string.Empty;
        [SerializeField] private string sourceLabel = string.Empty;
        [SerializeField] private CharacterDefinition decisionMaker;
        [SerializeField] private string decisionKey = string.Empty;
        [SerializeField] private List<WorldFlagRequirement> triggerConditions = new List<WorldFlagRequirement>();
        [SerializeField] private List<DecisionOptionDefinition> options = new List<DecisionOptionDefinition>();

        public string EventId => eventId;
        public string Title => title;
        public string Prompt => prompt;
        public string SourceLabel => sourceLabel;
        public CharacterDefinition DecisionMaker => decisionMaker;
        public string DecisionKey => decisionKey;
        public IReadOnlyList<WorldFlagRequirement> TriggerConditions => triggerConditions;
        public IReadOnlyList<DecisionOptionDefinition> Options => options;
    }
}
