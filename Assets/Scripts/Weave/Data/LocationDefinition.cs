using UnityEngine;
using System.Collections.Generic;

namespace Weave.Data
{
    public enum LocationType
    {
        Home,
        Farm,
        Mine,
        Forest,
        Workshop,
        Shop,
        Village
    }

    [CreateAssetMenu(menuName = "Weave/Location Definition")]
    public sealed class LocationDefinition : ScriptableObject
    {
        [SerializeField, HideInInspector] private string locationId = string.Empty;
        [SerializeField] private string id = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private LocationType locationType = LocationType.Village;
        [SerializeField] private GameObject buildingPrefab;
        [SerializeField] private List<TaskDefinition> defaultTasks = new List<TaskDefinition>();
        [SerializeField] private List<string> tags = new List<string>();
        [SerializeField, Min(1)] private int footprintWidth = 1;
        [SerializeField, Min(1)] private int footprintHeight = 1;
        [SerializeField] private Vector2 mapPosition;

        public string Id => string.IsNullOrWhiteSpace(id) ? locationId : id;
        public string LocationId => Id;
        public string DisplayName => displayName;
        public LocationType LocationType => locationType;
        public GameObject BuildingPrefab => buildingPrefab;
        public IReadOnlyList<TaskDefinition> DefaultTasks => defaultTasks;
        public IReadOnlyList<string> Tags => tags;
        public int FootprintWidth => Mathf.Max(1, footprintWidth);
        public int FootprintHeight => Mathf.Max(1, footprintHeight);
        public Vector2 MapPosition => mapPosition;
    }
}
