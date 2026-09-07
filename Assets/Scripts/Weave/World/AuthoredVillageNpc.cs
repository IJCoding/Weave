using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;

namespace Weave.World
{
    public sealed class AuthoredVillageNpc : VillageGridEntity
    {
        [SerializeField] private CharacterDefinition characterDefinition;
        [SerializeField] private string displayNameOverride = string.Empty;
        [SerializeField] private AuthoredVillageLocation startingLocation;
        [SerializeField] private AuthoredVillageLocation homeLocation;
        [SerializeField] private EventDefinition talkEvent;
        [SerializeField] private float talkDurationSeconds = 4f;
        [SerializeField] private bool interactionAvailable = true;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private TextMesh labelMesh;
        [SerializeField] private Color fallbackColor = new Color(0.92f, 0.74f, 0.29f, 1f);

        private CharacterDefinition runtimeCharacterDefinition;

        public CharacterDefinition CharacterDefinition => EnsureCharacterDefinition();
        public string CharacterId => EnsureCharacterDefinition() != null ? EnsureCharacterDefinition().CharacterId : string.Empty;
        public string DisplayName => !string.IsNullOrWhiteSpace(displayNameOverride)
            ? displayNameOverride
            : characterDefinition != null && !string.IsNullOrWhiteSpace(characterDefinition.DisplayName)
                ? characterDefinition.DisplayName
                : gameObject.name;
        public AuthoredVillageLocation StartingLocation => startingLocation;
        public AuthoredVillageLocation HomeLocation => homeLocation;
        public EventDefinition TalkEvent => talkEvent;
        public float TalkDurationSeconds => Mathf.Max(0.1f, talkDurationSeconds);
        public bool InteractionAvailable => interactionAvailable;

        private CharacterDefinition EnsureCharacterDefinition()
        {
            if (characterDefinition != null && !string.IsNullOrWhiteSpace(characterDefinition.CharacterId))
            {
                return characterDefinition;
            }

            if (runtimeCharacterDefinition != null)
            {
                return runtimeCharacterDefinition;
            }

            runtimeCharacterDefinition = ScriptableObject.CreateInstance<CharacterDefinition>();

            var sourceName = !string.IsNullOrWhiteSpace(displayNameOverride)
                ? displayNameOverride
                : characterDefinition != null && !string.IsNullOrWhiteSpace(characterDefinition.DisplayName)
                    ? characterDefinition.DisplayName
                    : gameObject.name;
            var fallbackId = sourceName.Trim().Replace(' ', '_').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(fallbackId))
            {
                fallbackId = gameObject.name.Trim().Replace(' ', '_').ToLowerInvariant();
            }

            var profession = characterDefinition != null ? characterDefinition.Profession : ProfessionType.Villager;
            var mapColor = characterDefinition != null ? characterDefinition.MapColor : fallbackColor;
            var homeDefinition = homeLocation != null ? homeLocation.LocationDefinition : characterDefinition != null ? characterDefinition.HomeLocation : null;
            var homeLocationId = homeLocation != null
                ? homeLocation.LocationId
                : characterDefinition != null ? characterDefinition.HomeLocationId : string.Empty;
            var startingResources = characterDefinition != null
                ? new List<ResourceAmount>(characterDefinition.StartingResources)
                : new List<ResourceAmount>();
            var developerCanon = characterDefinition != null
                ? new List<CanonDecisionDefault>(characterDefinition.DeveloperCanon)
                : new List<CanonDecisionDefault>();

            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "characterId", fallbackId);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "displayName", sourceName);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "profession", profession);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "mapColor", mapColor);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "homeLocation", homeDefinition);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "homeLocationId", homeLocationId);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "startingResources", startingResources);
            SerializedFieldUtility.SetPrivateField(runtimeCharacterDefinition, "developerCanon", developerCanon);
            return runtimeCharacterDefinition;
        }

        public void ApplyRuntimePosition(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        protected override void Awake()
        {
            runtimeCharacterDefinition = null;
            base.Awake();
            EnsureReferences();
            ApplyVisuals();
        }

        protected override void OnValidate()
        {
            runtimeCharacterDefinition = null;
            base.OnValidate();
            EnsureReferences();
            ApplyVisuals();
        }

        private void OnDrawGizmosSelected()
        {
            var start = startingLocation != null ? startingLocation.TravelAnchorPosition : (Vector2)transform.position;
            var home = homeLocation != null ? homeLocation.TravelAnchorPosition : start;
            Gizmos.color = new Color(0.93f, 0.73f, 0.25f, 1f);
            Gizmos.DrawLine(start, home);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(transform.position + Vector3.up * 0.3f, $"{DisplayName} ({CharacterId})");
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
                if (labelMesh == null)
                {
                    var labelObject = new GameObject("Label");
                    labelObject.transform.SetParent(transform, false);
                    labelObject.transform.localPosition = new Vector3(0f, -0.55f, 0f);
                    labelMesh = labelObject.AddComponent<TextMesh>();
                }
            }
        }

        private void ApplyVisuals()
        {
            EnsureCharacterDefinition();

            if (visualRenderer != null)
            {
                if (visualRenderer.sprite == null)
                {
                    visualRenderer.sprite = PrototypeSpriteLibrary.GetCircleSprite();
                }

                visualRenderer.color = characterDefinition != null ? characterDefinition.MapColor : fallbackColor;
                visualRenderer.sortingOrder = 10;
            }

            if (labelMesh != null)
            {
                labelMesh.text = DisplayName;
                labelMesh.anchor = TextAnchor.MiddleCenter;
                labelMesh.alignment = TextAlignment.Center;
                labelMesh.characterSize = 0.12f;
                labelMesh.fontSize = 48;
                labelMesh.color = Color.white;
            }
        }
    }
}
