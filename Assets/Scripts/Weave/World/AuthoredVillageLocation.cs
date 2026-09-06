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

    [ExecuteAlways]
    public sealed class AuthoredVillageLocation : MonoBehaviour
    {
        public static event Action<AuthoredVillageLocation> Clicked;

        [SerializeField] private string locationId = string.Empty;
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string visualLabel = string.Empty;
        [SerializeField] private LocationType locationType = LocationType.Village;
        [SerializeField] private LocationDefinition locationDefinition;
        [SerializeField] private List<TaskDefinition> availableTasks = new List<TaskDefinition>();
        [SerializeField] private bool isHome;
        [SerializeField] private AuthoredVillageNpc ownerNpc;
        [SerializeField] private Transform travelAnchor;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private TextMesh labelMesh;
        [SerializeField] private Color normalColor = new Color(0.26f, 0.34f, 0.44f, 1f);
        [SerializeField] private Color currentColor = new Color(0.92f, 0.95f, 0.99f, 1f);
        [SerializeField] private Color destinationColor = new Color(0.33f, 0.85f, 0.52f, 1f);

        public string LocationId => locationId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
        public string VisualLabel => string.IsNullOrWhiteSpace(visualLabel) ? DisplayName : visualLabel;
        public LocationType LocationType => locationType;
        public LocationDefinition LocationDefinition => locationDefinition;
        public IReadOnlyList<TaskDefinition> AvailableTasks => availableTasks;
        public bool IsHome => isHome;
        public AuthoredVillageNpc OwnerNpc => ownerNpc;
        public Vector2 WorldPosition => transform.position;
        public Vector2 TravelAnchorPosition => travelAnchor != null ? travelAnchor.position : transform.position;

        public void SetVisualState(LocationVisualState state)
        {
            EnsureReferences();
            if (visualRenderer == null)
            {
                return;
            }

            switch (state)
            {
                case LocationVisualState.Current:
                    visualRenderer.color = currentColor;
                    break;
                case LocationVisualState.Destination:
                    visualRenderer.color = destinationColor;
                    break;
                default:
                    visualRenderer.color = normalColor;
                    break;
            }
        }

        public Bounds GetBounds()
        {
            EnsureReferences();
            var collider = GetComponent<BoxCollider2D>();
            return collider != null ? collider.bounds : new Bounds(transform.position, Vector3.one);
        }

        public LocationDefinition CreateRuntimeDefinition()
        {
            var definition = ScriptableObject.CreateInstance<LocationDefinition>();
            definition.hideFlags = HideFlags.HideAndDontSave;
            SerializedFieldUtility.SetPrivateField(definition, "locationId", locationId);
            SerializedFieldUtility.SetPrivateField(definition, "displayName", DisplayName);
            SerializedFieldUtility.SetPrivateField(definition, "locationType", locationType);
            SerializedFieldUtility.SetPrivateField(definition, "mapPosition", (Vector2)transform.position);
            return definition;
        }

        private void Reset()
        {
            EnsureReferences();
            ConfigureCollider();
            ApplyAuthoringVisuals();
        }

        private void Awake()
        {
            EnsureReferences();
            ApplyAuthoringVisuals();
        }

        private void OnValidate()
        {
            EnsureReferences();
            ConfigureCollider();
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
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(TravelAnchorPosition, 0.14f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(TravelAnchorPosition + Vector2.up * 0.2f, locationId);
#endif
        }

        private void EnsureReferences()
        {
            if (visualRenderer == null)
            {
                visualRenderer = GetComponent<SpriteRenderer>();
                if (visualRenderer == null)
                {
                    visualRenderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }

            if (labelMesh == null)
            {
                labelMesh = GetComponentInChildren<TextMesh>();
                if (labelMesh == null)
                {
                    var labelObject = new GameObject("Label");
                    labelObject.transform.SetParent(transform, false);
                    labelObject.transform.localPosition = new Vector3(0f, 0.95f, 0f);
                    labelMesh = labelObject.AddComponent<TextMesh>();
                }
            }

            if (travelAnchor == null)
            {
                var anchor = transform.Find("TravelAnchor");
                if (anchor == null)
                {
                    var anchorObject = new GameObject("TravelAnchor");
                    anchorObject.transform.SetParent(transform, false);
                    anchorObject.transform.localPosition = new Vector3(0f, -0.75f, 0f);
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

            collider.size = new Vector2(1.6f, 1f);
        }

        private void ApplyAuthoringVisuals()
        {
            if (visualRenderer != null)
            {
                visualRenderer.sprite = PrototypeSpriteLibrary.GetSquareSprite();
                visualRenderer.drawMode = SpriteDrawMode.Sliced;
                visualRenderer.size = new Vector2(1.6f, 1f);
                visualRenderer.color = normalColor;
                visualRenderer.sortingOrder = 0;
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
