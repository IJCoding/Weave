using UnityEngine;
using Weave.Data;

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
        [SerializeField] private Sprite authoredSprite;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private TextMesh labelMesh;
        [SerializeField] private Color fallbackColor = new Color(0.92f, 0.74f, 0.29f, 1f);

        [System.NonSerialized] private bool warnedMissingCharacterDefinition;
        [System.NonSerialized] private bool warnedEmptyCharacterId;
        [System.NonSerialized] private bool warnedMissingGrid;
        [System.NonSerialized] private string fallbackCharacterId;

        public CharacterDefinition CharacterDefinition => characterDefinition;
        public string AuthoredCharacterId => characterDefinition != null && !string.IsNullOrWhiteSpace(characterDefinition.CharacterId)
            ? characterDefinition.CharacterId.Trim()
            : string.Empty;
        public string CharacterId => !string.IsNullOrWhiteSpace(AuthoredCharacterId) ? AuthoredCharacterId : ResolveFallbackCharacterId();
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

        public void ApplyRuntimePosition(Vector2 position)
        {
            transform.position = new Vector3(position.x, position.y, transform.position.z);
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            ApplyVisuals();
            ValidateAuthoringConfiguration();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureReferences();
            ApplyVisuals();
            ValidateAuthoringConfiguration();
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
            if (visualRenderer != null)
            {
                if (visualRenderer.sprite == null)
                {
                    visualRenderer.sprite = authoredSprite != null ? authoredSprite : PrototypeSpriteLibrary.GetCircleSprite();
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

        private void ValidateAuthoringConfiguration()
        {
            if (characterDefinition == null)
            {
                if (!warnedMissingCharacterDefinition)
                {
                    Debug.LogWarning($"NPC '{name}' is missing a CharacterDefinition reference.", this);
                    warnedMissingCharacterDefinition = true;
                }
            }
            else
            {
                warnedMissingCharacterDefinition = false;
            }

            if (characterDefinition != null && string.IsNullOrWhiteSpace(characterDefinition.CharacterId))
            {
                if (!warnedEmptyCharacterId)
                {
                    Debug.LogError($"NPC '{name}' has a CharacterDefinition with an empty CharacterId.", this);
                    warnedEmptyCharacterId = true;
                }
            }
            else
            {
                warnedEmptyCharacterId = false;
            }

            if (Grid == null)
            {
                if (!warnedMissingGrid)
                {
                    Debug.LogError($"NPC '{name}' could not find a VillageGrid in the scene hierarchy.", this);
                    warnedMissingGrid = true;
                }
            }
            else
            {
                warnedMissingGrid = false;
            }

            private string ResolveFallbackCharacterId()
            {
                if (!string.IsNullOrWhiteSpace(fallbackCharacterId))
                {
                    return fallbackCharacterId;
                }

                var source = string.IsNullOrWhiteSpace(gameObject.name) ? "npc" : gameObject.name;
                fallbackCharacterId = source.Trim().Replace(' ', '_').ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(fallbackCharacterId))
                {
                    fallbackCharacterId = "npc";
                }

                return fallbackCharacterId;
            }
        }
    }
}
