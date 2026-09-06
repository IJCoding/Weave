using UnityEngine;

namespace Weave.World
{
    [ExecuteAlways]
    public sealed class AuthoredVillageRoadTile : MonoBehaviour
    {
        [SerializeField] private float movementMultiplier = 1.5f;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private Color roadColor = new Color(0.55f, 0.45f, 0.31f, 1f);
        [SerializeField] private Vector2 size = new Vector2(1f, 1f);

        public float MovementMultiplier => Mathf.Max(1f, movementMultiplier);
        public Bounds Bounds
        {
            get
            {
                EnsureReferences();
                ApplyVisuals();
                return GetComponent<BoxCollider2D>().bounds;
            }
        }

        public bool Contains(Vector2 point)
        {
            return Bounds.Contains(point);
        }

        private void Reset()
        {
            EnsureReferences();
            ApplyVisuals();
        }

        private void Awake()
        {
            EnsureReferences();
            ApplyVisuals();
        }

        private void OnValidate()
        {
            EnsureReferences();
            ApplyVisuals();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.80f, 0.67f, 0.26f, 1f);
            Gizmos.DrawWireCube(transform.position, size);
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
        }

        private void ApplyVisuals()
        {
            if (visualRenderer != null)
            {
                visualRenderer.sprite = PrototypeSpriteLibrary.GetSquareSprite();
                visualRenderer.drawMode = SpriteDrawMode.Sliced;
                visualRenderer.size = size;
                visualRenderer.color = roadColor;
                visualRenderer.sortingOrder = -1;
            }

            var collider = GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<BoxCollider2D>();
            }

            collider.size = size;
            collider.isTrigger = true;
        }
    }
}
