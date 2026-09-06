using UnityEngine;

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
        [SerializeField] private string locationId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private LocationType locationType = LocationType.Village;
        [SerializeField] private Vector2 mapPosition;

        public string LocationId => locationId;
        public string DisplayName => displayName;
        public LocationType LocationType => locationType;
        public Vector2 MapPosition => mapPosition;
    }
}
