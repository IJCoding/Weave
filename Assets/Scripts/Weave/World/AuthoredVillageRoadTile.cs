using UnityEngine;

namespace Weave.World
{
    public sealed class AuthoredVillageRoadTile : VillageGridEntity
    {
        [SerializeField] private float movementMultiplier = 1.5f;
        [SerializeField] private SpriteRenderer visualRenderer;
        [SerializeField] private Color roadColor = new Color(0.55f, 0.45f, 0.31f, 1f);

        public float MovementMultiplier => Mathf.Max(1f, movementMultiplier);

        public Bounds Bounds
        {
            get
            {
                var grid = Grid;
                var size = grid != null ? grid.CellSize : 1f;
                return new Bounds(transform.position, new Vector3(size, size, 0.1f));
            }
        }

        public bool Contains(Vector2 point)
        {
            var grid = Grid;
            if (grid == null)
            {
                return Bounds.Contains(point);
            }

            return grid.WorldToGrid(point) == GridPosition;
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            ApplyVisuals();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            EnsureReferences();
            ApplyVisuals();
        }

        private void OnDrawGizmosSelected()
        {
            var grid = Grid;
            var size = grid != null ? grid.CellSize : 1f;
            Gizmos.color = new Color(0.80f, 0.67f, 0.26f, 1f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * size);
        }

        private void EnsureReferences()
        {
            if (visualRenderer == null)
            {
                visualRenderer = GetComponent<SpriteRenderer>();
            }
        }

        private void ApplyVisuals()
        {
            if (visualRenderer != null)
            {
                visualRenderer.color = roadColor;
                visualRenderer.sortingOrder = -1;
            }

            var collider = GetComponent<BoxCollider2D>();
            if (collider == null)
            {
                collider = gameObject.AddComponent<BoxCollider2D>();
            }

            var grid = Grid;
            var size = grid != null ? grid.CellSize : 1f;
            collider.size = new Vector2(size, size);
            collider.isTrigger = true;
        }
    }
}
