using System;
using System.Collections.Generic;
using UnityEngine;

namespace Weave.Data
{
    public enum ProfessionType
    {
        Farmer,
        Miner,
        Lumberjack,
        Blacksmith,
        Shopkeeper,
        Villager
    }

    [Serializable]
    public struct ResourceAmount
    {
        public string ResourceId;
        public int Amount;
    }

    [Serializable]
    public struct CanonDecisionDefault
    {
        public string DecisionKey;
        public string DefaultOptionId;
    }

    [CreateAssetMenu(menuName = "Weave/Character Definition")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [SerializeField] private string characterId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private ProfessionType profession = ProfessionType.Villager;
        [SerializeField] private Color mapColor = Color.white;
        [SerializeField] private LocationDefinition homeLocation;
        [SerializeField] private string homeLocationId = string.Empty;
        [SerializeField] private List<ResourceAmount> startingResources = new List<ResourceAmount>();
        [SerializeField] private List<CanonDecisionDefault> developerCanon = new List<CanonDecisionDefault>();

        public string CharacterId => characterId;
        public string DisplayName => displayName;
        public ProfessionType Profession => profession;
        public Color MapColor => mapColor;
        public LocationDefinition HomeLocation => homeLocation;
        public string HomeLocationId => !string.IsNullOrWhiteSpace(homeLocationId)
            ? homeLocationId
            : homeLocation != null ? homeLocation.LocationId : string.Empty;
        public IReadOnlyList<ResourceAmount> StartingResources => startingResources;
        public IReadOnlyList<CanonDecisionDefault> DeveloperCanon => developerCanon;
    }
}
