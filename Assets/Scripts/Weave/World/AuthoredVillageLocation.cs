using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using Weave.Data;
using Weave.Runtime;

namespace Weave.World
{
    public enum LocationVisualState
    {
        Normal,
        Current,
        Destination
    }

    public enum LocationPlacementSource
    {
        Preset,
        Generated
    }

    public sealed class AuthoredVillageLocation : VillageGridEntity
    {
        public static event Action<AuthoredVillageLocation> Clicked;

        [SerializeField, HideInInspector] private string locationId = string.Empty;
        [SerializeField] private string instanceId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string visualLabel = string.Empty;
        [SerializeField] private LocationType locationType = LocationType.Village;
        [SerializeField] private LocationDefinition locationDefinition;
        [SerializeField] private bool useDefaultTasks = true;
        [SerializeField] private List<TaskDefinition> availableTasks = new List<TaskDefinition>();
        [SerializeField] private List<TaskDefinition> additionalTasks = new List<TaskDefinition>();
        [SerializeField] private bool isHome;
        [SerializeField] private AuthoredVillageNpc ownerNpc;
        [SerializeField] private Transform travelAnchor;
        [SerializeField] private Vector2Int travelGridOffset = new Vector2Int(0, -1);
        [SerializeField, Min(1)] private int footprintWidth = 1;
        [SerializeField, Min(1)] private int footprintHeight = 1;
        [SerializeField] private LocationPlacementSource placementSource = LocationPlacementSource.Preset;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private TextMesh labelMesh;
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color currentColor = new Color(0.92f, 0.95f, 0.99f, 1f);
        [SerializeField] private Color destinationColor = new Color(0.33f, 0.85f, 0.52f, 1f);

        public string InstanceId => string.IsNullOrWhiteSpace(instanceId) ? locationId : instanceId;
        public string LocationId => InstanceId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? locationDefinition != null && !string.IsNullOrWhiteSpace(locationDefinition.DisplayName)
                ? locationDefinition.DisplayName
                : gameObject.name
            : displayName;
        public string VisualLabel => string.IsNullOrWhiteSpace(visualLabel) ? DisplayName : visualLabel;
        public LocationType LocationType => locationType;
        public LocationDefinition LocationDefinition => locationDefinition;
        public bool UseDefaultTasks => useDefaultTasks;
        public IReadOnlyList<TaskDefinition> AvailableTasks => availableTasks;
        public IReadOnlyList<TaskDefinition> AdditionalTasks => additionalTasks;
        public bool IsHome => isHome;
        public AuthoredVillageNpc OwnerNpc => ownerNpc;
        public int FootprintWidth => Mathf.Max(1, locationDefinition != null ? locationDefinition.FootprintWidth : footprintWidth);
        public int FootprintHeight => Mathf.Max(1, locationDefinition != null ? locationDefinition.FootprintHeight : footprintHeight);
        public LocationPlacementSource PlacementSource => placementSource;
        public Vector2Int TravelGridPosition => GridPosition + travelGridOffset;
        public Vector2 TravelAnchorPosition => travelAnchor != null ? travelAnchor.position : ResolveWorldFromGridPosition(TravelGridPosition);

        public IEnumerable<Vector2Int> EnumerateFootprintCells()
        {
            for (var x = 0; x < FootprintWidth; x++)
            {
                for (var y = 0; y < FootprintHeight; y++)
                {
                    yield return GridPosition + new Vector2Int(x, y);
                }
            }
        }

        public IEnumerable<TaskDefinition> GetAllTasks()
        {
            if (useDefaultTasks && locationDefinition != null)
            {
                foreach (var task in locationDefinition.DefaultTasks)
                {
                    if (task != null)
                    {
                        yield return task;
                    }
                }
            }
            else
            {
                foreach (var task in availableTasks)
                {
                    if (task != null)
                    {
                        yield return task;
                    }
                }
            }

            foreach (var task in additionalTasks)
            {
                if (task != null)
                {
                    yield return task;
                }
            }
        }

        public void ConfigureGenerated(string newInstanceId, LocationDefinition definition, Vector2Int gridPosition)
        {
            instanceId = newInstanceId;
            locationId = newInstanceId;
            locationDefinition = definition;
            displayName = definition != null ? definition.DisplayName : displayName;
            locationType = definition != null ? definition.LocationType : locationType;
            placementSource = LocationPlacementSource.Generated;
            SetGridPosition(gridPosition);
            ApplyTravelAnchor();
            ConfigureCollider();
        }

        public void SetVisualState(LocationVisualState state)
        {
            EnsureReferences();
            if (visualRenderer == null)
            {
                return;
            }

            visualRenderer.color = state switch
            {
                LocationVisualState.Current => currentColor,
                LocationVisualState.Destination => destinationColor,
                _ => normalColor
            };
        }

        public Bounds GetBounds(VillageGrid villageGrid = null)
        {
            villageGrid ??= Grid;
            if (villageGrid == null)
            {
                return new Bounds(transform.position, Vector3.one);
            }

            var min = villageGrid.GridToWorld(GridPosition);
            var max = villageGrid.GridToWorld(GridPosition + new Vector2Int(FootprintWidth - 1, FootprintHeight - 1));
            var center = (min + max) * 0.5f;
            var size = new Vector3(villageGrid.CellSize * FootprintWidth, villageGrid.CellSize * FootprintHeight, 0.1f);
            return new Bounds(center, size);
        }

        public LocationDefinition CreateRuntimeDefinition()
        {
            var definition = ScriptableObject.CreateInstance<LocationDefinition>();
            definition.hideFlags = HideFlags.HideAndDontSave;
            SerializedFieldUtility.SetPrivateField(definition, "locationId", InstanceId);
            SerializedFieldUtility.SetPrivateField(definition, "id", InstanceId);
            SerializedFieldUtility.SetPrivateField(definition, "displayName", DisplayName);
            SerializedFieldUtility.SetPrivateField(definition, "locationType", locationType);
            SerializedFieldUtility.SetPrivateField(definition, "buildingPrefab", locationDefinition != null ? locationDefinition.BuildingPrefab : gameObject);
            SerializedFieldUtility.SetPrivateField(definition, "defaultTasks", new List<TaskDefinition>(GetAllTasks()));
            SerializedFieldUtility.SetPrivateField(definition, "tags", new List<string>());
            SerializedFieldUtility.SetPrivateField(definition, "footprintWidth", FootprintWidth);
            SerializedFieldUtility.SetPrivateField(definition, "footprintHeight", FootprintHeight);
            SerializedFieldUtility.SetPrivateField(definition, "mapPosition", (Vector2)transform.position);
            return definition;
        }

        protected override void Awake()
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                instanceId = locationId;
            }

            locationId = instanceId;
            base.Awake();
            EnsureReferences();
            ConfigureCollider();
            ApplyTravelAnchor();
            ApplyAuthoringVisuals();
        }

        protected override void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(instanceId))
            {
                instanceId = locationId;
            }

            locationId = instanceId;
            base.OnValidate();
            EnsureReferences();
            ConfigureCollider();
            ApplyTravelAnchor();
            ApplyAuthoringVisuals();
        }

        private void OnMouseUpAsButton()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return;
            }

            Clicked?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            var grid = Grid;
            if (grid == null)
            {
                return;
            }

            Gizmos.color = new Color(0.3f, 1f, 0.8f, 1f);
            foreach (var cell in EnumerateFootprintCells())
            {
                Gizmos.DrawWireCube(grid.GridToWorld(cell), Vector3.one * grid.CellSize);
            }

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(TravelAnchorPosition, grid.CellSize * 0.16f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(TravelAnchorPosition + Vector2.up * 0.2f, instanceId);
#endif
        }

        private void EnsureReferences()
        {
            if (visualRenderer == null)
            {
                visualRenderer = GetComponent<SpriteRenderer>();
            }

            if (labelMesh == null)
            {
                labelMesh = GetComponentInChildren<TextMesh>();
            }

            if (travelAnchor == null)
            {
                var anchor = transform.Find("TravelAnchor");
                if (anchor == null)
                {
                    var anchorObject = new GameObject("TravelAnchor");
                    anchorObject.transform.SetParent(transform, false);
                    anchor = anchorObject.transform;
                }

                travelAnchor = anchor;
            }
        }

        private void ConfigureCollider()
        {
            var collider = GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<BoxCollider2D>();
            }

            var grid = Grid;
            var cellSize = grid != null ? grid.CellSize : 1f;
            collider.size = new Vector2(cellSize * FootprintWidth, cellSize * FootprintHeight);
            collider.offset = new Vector2(cellSize * (FootprintWidth - 1) * 0.5f, cellSize * (FootprintHeight - 1) * 0.5f);
        }

        private void ApplyTravelAnchor()
        {
            if (travelAnchor == null)
            {
                return;
            }

            var world = ResolveWorldFromGridPosition(TravelGridPosition);
            travelAnchor.position = new Vector3(world.x, world.y, travelAnchor.position.z);
        }

        private void ApplyAuthoringVisuals()
        {
            if (visualRenderer != null)
            {
                normalColor = visualRenderer.color;
            }

            if (labelMesh != null)
            {
                labelMesh.text = VisualLabel;
                labelMesh.anchor = TextAnchor.MiddleCenter;
                labelMesh.alignment = TextAlignment.Center;
                labelMesh.characterSize = 0.15f;
                labelMesh.fontSize = 48;
                labelMesh.color = Color.white;
            }
        }
    }
}
