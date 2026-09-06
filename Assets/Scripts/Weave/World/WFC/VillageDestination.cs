using System;
using UnityEngine;

namespace Weave.World.WFC
{
    public enum VillageLocationType
    {
        None = 0,
        Forest = 1,
        Mine = 2,
        Smithy = 3,
        Farm = 4,
        Inn = 5,
        Market = 6,
        Well = 7,
        House = 8,
        Lumberyard = 9
    }

    [Serializable]
    public sealed class VillageDestination
    {
        [SerializeField] private VillageLocationType locationType = VillageLocationType.None;
        [SerializeField] private Vector3Int gridPosition;
        [SerializeField] private PathSocketDirection entranceDirection = PathSocketDirection.None;

        public VillageLocationType LocationType
        {
            get => locationType;
            set => locationType = value;
        }

        public Vector3Int GridPosition
        {
            get => gridPosition;
            set => gridPosition = value;
        }

        public PathSocketDirection EntranceDirection
        {
            get => entranceDirection;
            set => entranceDirection = value;
        }
    }
}
