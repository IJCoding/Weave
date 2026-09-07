using UnityEngine;

namespace Weave.World
{
    [ExecuteAlways]
    public sealed class VillageGrid : MonoBehaviour
    {
        [SerializeField] private VillageGridSettings settings;
        [SerializeField] private Vector2 origin = Vector2.zero;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;
        [SerializeField, Min(1)] private int gizmoHalfExtentCells = 20;
        [SerializeField] private Color gizmoColor = new Color(0.4f, 0.65f, 1f, 0.35f);

        public float CellSize => settings != null ? settings.CellSize : Mathf.Max(0.1f, cellSize);
        public Vector2 Origin => settings != null ? settings.Origin : origin;

        public Vector2Int WorldToGrid(Vector2 world)
        {
            var size = CellSize;
            var local = (world - Origin) / size;
            return new Vector2Int(Mathf.RoundToInt(local.x), Mathf.RoundToInt(local.y));
        }

        public Vector2 GridToWorld(Vector2Int gridPosition)
        {
            return Origin + new Vector2(gridPosition.x * CellSize, gridPosition.y * CellSize);
        }

        public Vector2 SnapWorld(Vector2 world)
        {
            return GridToWorld(WorldToGrid(world));
        }

        public static VillageGrid FindGrid(Transform context)
        {
            if (context == null)
            {
                return null;
            }

            var grid = context.GetComponentInParent<VillageGrid>();
            return grid != null ? grid : Object.FindObjectOfType<VillageGrid>();
        }

        private void OnDrawGizmos()
        {
            var extent = Mathf.Max(1, gizmoHalfExtentCells);
            var size = CellSize;
            var start = Origin - Vector2.one * extent * size;
            var lineLength = extent * 2 * size;
            Gizmos.color = gizmoColor;

            for (var x = 0; x <= extent * 2; x++)
            {
                var px = start.x + x * size;
                Gizmos.DrawLine(new Vector3(px, start.y, 0f), new Vector3(px, start.y + lineLength, 0f));
            }

            for (var y = 0; y <= extent * 2; y++)
            {
                var py = start.y + y * size;
                Gizmos.DrawLine(new Vector3(start.x, py, 0f), new Vector3(start.x + lineLength, py, 0f));
            }
        }
    }
}
