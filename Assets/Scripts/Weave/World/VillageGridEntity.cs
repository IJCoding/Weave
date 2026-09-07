using UnityEngine;

namespace Weave.World
{
    [ExecuteAlways]
    public abstract class VillageGridEntity : MonoBehaviour
    {
        [SerializeField] private VillageGrid villageGrid;
        [SerializeField] private Vector2Int gridPosition;
        [SerializeField, HideInInspector] private bool hasAuthoritativeGridPosition;
        [SerializeField] private bool snapInEditor = true;

        private Vector3 lastSnappedWorldPosition;
        private bool initialized;

        public VillageGrid Grid => villageGrid != null ? villageGrid : VillageGrid.FindGrid(transform);
        public Vector2Int GridPosition => gridPosition;

        protected virtual void Awake()
        {
            SyncToGrid();
        }

        protected virtual void OnValidate()
        {
            hasAuthoritativeGridPosition = true;
            SyncToGrid();
        }

        protected virtual void Update()
        {
            if (Application.isPlaying || !snapInEditor)
            {
                return;
            }

            SyncToGrid();
        }

        public void SetGridPosition(Vector2Int position)
        {
            gridPosition = position;
            hasAuthoritativeGridPosition = true;
            SyncTransformFromGrid();
        }

        protected void SyncToGrid()
        {
            if (villageGrid == null)
            {
                villageGrid = VillageGrid.FindGrid(transform);
            }

            if (!initialized)
            {
                if (!hasAuthoritativeGridPosition)
                {
                    gridPosition = ResolveCurrentGridPosition();
                    hasAuthoritativeGridPosition = true;
                }

                SyncTransformFromGrid();
                initialized = true;
                return;
            }

            var current = transform.position;
            var snapped = ResolveWorldFromGridPosition(gridPosition);
            var snappedWorldPosition = new Vector3(snapped.x, snapped.y, current.z);

            if (Vector3.Distance(current, lastSnappedWorldPosition) > 0.01f)
            {
                gridPosition = ResolveCurrentGridPosition();
                hasAuthoritativeGridPosition = true;
                SyncTransformFromGrid();
                return;
            }

            if (Vector3.Distance(current, snappedWorldPosition) > 0.01f)
            {
                SyncTransformFromGrid();
            }
        }

        protected Vector2 ResolveWorldFromGridPosition(Vector2Int position)
        {
            var grid = Grid;
            return grid != null ? grid.GridToWorld(position) : new Vector2(position.x, position.y);
        }

        protected Vector2Int ResolveCurrentGridPosition()
        {
            var grid = Grid;
            return grid != null
                ? grid.WorldToGrid(transform.position)
                : new Vector2Int(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.y));
        }

        protected void SyncTransformFromGrid()
        {
            var world = ResolveWorldFromGridPosition(gridPosition);
            transform.position = new Vector3(world.x, world.y, transform.position.z);
            lastSnappedWorldPosition = transform.position;
            transform.hasChanged = false;
        }
    }
}
