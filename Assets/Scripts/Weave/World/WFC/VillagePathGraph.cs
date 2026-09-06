using System.Collections.Generic;
using UnityEngine;

namespace Weave.World.WFC
{
    public sealed class VillagePathGraph : MonoBehaviour
    {
        [SerializeField] private int width;
        [SerializeField] private int height;
        [SerializeField] private int connectedPathCellCount;
        [SerializeField] private bool allRequiredDestinationsReachable;
        [SerializeField] private List<VillageDestination> destinations = new List<VillageDestination>();

        private WFCPathTileDefinition[,] tileGrid;
        private readonly Dictionary<VillageLocationType, List<VillageDestination>> destinationsByType =
            new Dictionary<VillageLocationType, List<VillageDestination>>();

        public int ConnectedPathCellCount => connectedPathCellCount;
        public bool AllRequiredDestinationsReachable => allRequiredDestinationsReachable;
        public IReadOnlyList<VillageDestination> Destinations => destinations;

        public void Build(
            WFCPathTileDefinition[,] generatedTiles,
            int generatedWidth,
            int generatedHeight,
            List<VillageDestination> generatedDestinations,
            List<VillageLocationType> requiredDestinationTypes)
        {
            tileGrid = generatedTiles;
            width = generatedWidth;
            height = generatedHeight;
            destinations = generatedDestinations != null ? new List<VillageDestination>(generatedDestinations) : new List<VillageDestination>();

            destinationsByType.Clear();
            foreach (var destination in destinations)
            {
                if (!destinationsByType.TryGetValue(destination.LocationType, out var typedDestinations))
                {
                    typedDestinations = new List<VillageDestination>();
                    destinationsByType.Add(destination.LocationType, typedDestinations);
                }

                typedDestinations.Add(destination);
            }

            connectedPathCellCount = CountLargestConnectedPathComponent(tileGrid, width, height);
            allRequiredDestinationsReachable = AreRequiredDestinationsConnected(tileGrid, width, height, destinations, requiredDestinationTypes);
        }

        public VillageDestination GetDestination(VillageLocationType type)
        {
            if (destinationsByType.TryGetValue(type, out var list) && list.Count > 0)
            {
                return list[0];
            }

            return null;
        }

        public List<Vector3Int> FindPathToDestination(Vector3Int start, VillageLocationType destinationType)
        {
            if (!destinationsByType.TryGetValue(destinationType, out var typedDestinations) || typedDestinations.Count == 0)
            {
                return new List<Vector3Int>();
            }

            var targetSet = new HashSet<Vector3Int>();
            foreach (var destination in typedDestinations)
            {
                targetSet.Add(destination.GridPosition);
            }

            return FindPathToAny(start, targetSet);
        }

        public List<Vector3Int> FindPath(Vector3Int start, Vector3Int destination)
        {
            var targets = new HashSet<Vector3Int> { destination };
            return FindPathToAny(start, targets);
        }

        public bool CanMove(Vector3Int from, Vector3Int to)
        {
            if (tileGrid == null)
            {
                return false;
            }

            if (!IsInside(from.x, from.y) || !IsInside(to.x, to.y))
            {
                return false;
            }

            var delta = to - from;
            if (Mathf.Abs(delta.x) + Mathf.Abs(delta.y) != 1)
            {
                return false;
            }

            return CanMove(tileGrid[from.x, from.y], tileGrid[to.x, to.y], PathSocketDirectionUtility.FromDelta(delta));
        }

        public static bool CanMove(WFCPathTileDefinition from, WFCPathTileDefinition to, PathSocketDirection direction)
        {
            if (from == null || to == null || direction == PathSocketDirection.None)
            {
                return false;
            }

            if (!from.IsWalkable || !to.IsWalkable)
            {
                return false;
            }

            return from.HasConnection(direction) && to.HasConnection(PathSocketDirectionUtility.Opposite(direction));
        }

        public static bool AreTilesCompatible(WFCPathTileDefinition leftTile, WFCPathTileDefinition rightTile, PathSocketDirection directionFromLeftToRight)
        {
            if (leftTile == null || rightTile == null)
            {
                return false;
            }

            if (leftTile.IsBlock || rightTile.IsBlock)
            {
                return true;
            }

            var leftHasSocket = leftTile.HasConnection(directionFromLeftToRight);
            var rightHasSocket = rightTile.HasConnection(PathSocketDirectionUtility.Opposite(directionFromLeftToRight));
            return leftHasSocket == rightHasSocket;
        }

        public static int CountLargestConnectedPathComponent(WFCPathTileDefinition[,] grid, int mapWidth, int mapHeight)
        {
            if (grid == null)
            {
                return 0;
            }

            var visited = new bool[mapWidth, mapHeight];
            var bestCount = 0;

            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    if (visited[x, y] || !IsWalkableTile(grid[x, y]))
                    {
                        continue;
                    }

                    var count = FloodCount(grid, mapWidth, mapHeight, x, y, visited);
                    if (count > bestCount)
                    {
                        bestCount = count;
                    }
                }
            }

            return bestCount;
        }

        public static bool AreRequiredDestinationsConnected(
            WFCPathTileDefinition[,] grid,
            int mapWidth,
            int mapHeight,
            List<VillageDestination> allDestinations,
            List<VillageLocationType> requiredTypes)
        {
            if (grid == null)
            {
                return false;
            }

            if (requiredTypes == null || requiredTypes.Count == 0)
            {
                return true;
            }

            var requiredTypeSet = new HashSet<VillageLocationType>();
            foreach (var requiredType in requiredTypes)
            {
                if (requiredType != VillageLocationType.None)
                {
                    requiredTypeSet.Add(requiredType);
                }
            }

            if (requiredTypeSet.Count == 0)
            {
                return true;
            }

            var requiredPositions = new List<Vector3Int>();
            var encounteredTypes = new HashSet<VillageLocationType>();

            if (allDestinations != null)
            {
                foreach (var destination in allDestinations)
                {
                    if (requiredTypeSet.Contains(destination.LocationType) && encounteredTypes.Add(destination.LocationType))
                    {
                        requiredPositions.Add(destination.GridPosition);
                    }
                }
            }

            foreach (var requiredType in requiredTypeSet)
            {
                if (!encounteredTypes.Contains(requiredType))
                {
                    return false;
                }
            }

            if (requiredPositions.Count <= 1)
            {
                return true;
            }

            var reachable = GetReachableCells(grid, mapWidth, mapHeight, requiredPositions[0]);
            for (var i = 1; i < requiredPositions.Count; i++)
            {
                if (!reachable.Contains(requiredPositions[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private List<Vector3Int> FindPathToAny(Vector3Int start, HashSet<Vector3Int> targets)
        {
            var empty = new List<Vector3Int>();
            if (tileGrid == null || targets == null || targets.Count == 0)
            {
                return empty;
            }

            if (!IsInside(start.x, start.y) || !IsWalkableTile(tileGrid[start.x, start.y]))
            {
                return empty;
            }

            if (targets.Contains(start))
            {
                return new List<Vector3Int> { start };
            }

            var queue = new Queue<Vector3Int>();
            var visited = new bool[width, height];
            var parent = new Dictionary<Vector3Int, Vector3Int>();
            queue.Enqueue(start);
            visited[start.x, start.y] = true;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in EnumerateDirections())
                {
                    var offset = PathSocketDirectionUtility.ToOffset(direction);
                    var next = new Vector3Int(current.x + offset.x, current.y + offset.y, 0);

                    if (!IsInside(next.x, next.y) || visited[next.x, next.y])
                    {
                        continue;
                    }

                    if (!CanMove(current, next))
                    {
                        continue;
                    }

                    visited[next.x, next.y] = true;
                    parent[next] = current;

                    if (targets.Contains(next))
                    {
                        return ReconstructPath(start, next, parent);
                    }

                    queue.Enqueue(next);
                }
            }

            return empty;
        }

        private List<Vector3Int> ReconstructPath(Vector3Int start, Vector3Int end, Dictionary<Vector3Int, Vector3Int> parent)
        {
            var result = new List<Vector3Int> { end };
            var current = end;

            while (current != start)
            {
                if (!parent.TryGetValue(current, out var previous))
                {
                    return new List<Vector3Int>();
                }

                current = previous;
                result.Add(current);
            }

            result.Reverse();
            return result;
        }

        private bool IsInside(int x, int y)
        {
            return x >= 0 && x < width && y >= 0 && y < height;
        }

        private static int FloodCount(WFCPathTileDefinition[,] grid, int mapWidth, int mapHeight, int startX, int startY, bool[,] visited)
        {
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(new Vector3Int(startX, startY, 0));
            visited[startX, startY] = true;

            var count = 0;
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                count++;

                foreach (var direction in EnumerateDirections())
                {
                    var offset = PathSocketDirectionUtility.ToOffset(direction);
                    var nx = current.x + offset.x;
                    var ny = current.y + offset.y;
                    if (nx < 0 || nx >= mapWidth || ny < 0 || ny >= mapHeight || visited[nx, ny])
                    {
                        continue;
                    }

                    if (!CanMove(grid[current.x, current.y], grid[nx, ny], direction))
                    {
                        continue;
                    }

                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector3Int(nx, ny, 0));
                }
            }

            return count;
        }

        private static HashSet<Vector3Int> GetReachableCells(WFCPathTileDefinition[,] grid, int mapWidth, int mapHeight, Vector3Int start)
        {
            var reachable = new HashSet<Vector3Int>();
            if (start.x < 0 || start.x >= mapWidth || start.y < 0 || start.y >= mapHeight)
            {
                return reachable;
            }

            if (!IsWalkableTile(grid[start.x, start.y]))
            {
                return reachable;
            }

            var queue = new Queue<Vector3Int>();
            queue.Enqueue(start);
            reachable.Add(start);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var direction in EnumerateDirections())
                {
                    var offset = PathSocketDirectionUtility.ToOffset(direction);
                    var next = new Vector3Int(current.x + offset.x, current.y + offset.y, 0);
                    if (next.x < 0 || next.x >= mapWidth || next.y < 0 || next.y >= mapHeight)
                    {
                        continue;
                    }

                    if (reachable.Contains(next))
                    {
                        continue;
                    }

                    if (!CanMove(grid[current.x, current.y], grid[next.x, next.y], direction))
                    {
                        continue;
                    }

                    reachable.Add(next);
                    queue.Enqueue(next);
                }
            }

            return reachable;
        }

        private static bool IsWalkableTile(WFCPathTileDefinition tile)
        {
            return tile != null && tile.IsWalkable;
        }

        private static IEnumerable<PathSocketDirection> EnumerateDirections()
        {
            yield return PathSocketDirection.Top;
            yield return PathSocketDirection.Bottom;
            yield return PathSocketDirection.Left;
            yield return PathSocketDirection.Right;
        }
    }
}
