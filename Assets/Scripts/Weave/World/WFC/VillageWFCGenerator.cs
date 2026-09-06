using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Weave.World.WFC
{
    public sealed class VillageWFCGenerator : MonoBehaviour
    {
        [Serializable]
        private sealed class DestinationTypeRule
        {
            public VillageLocationType Type = VillageLocationType.House;
            public bool AllowMultiple = true;
            public int Weight = 1;
        }

        [Header("References")]
        [SerializeField] private WFCPathLibrary pathLibrary;
        [SerializeField] private Tilemap targetTilemap;
        [SerializeField] private VillagePathGraph villagePathGraph;

        [Header("Map")]
        [SerializeField, Min(1)] private int mapWidth = 64;
        [SerializeField, Min(1)] private int mapHeight = 64;

        [Header("Random")]
        [SerializeField] private bool useRandomSeed = true;
        [SerializeField] private int seed = 12345;
        [SerializeField, Min(1)] private int maxRetries = 50;

        [Header("Path Ends")]
        [SerializeField, Min(0)] private int minimumPathEnds = 4;
        [SerializeField, Min(0)] private int maximumPathEnds = 32;

        [Header("Destinations")]
        [SerializeField] private List<VillageLocationType> requiredDestinationTypes = new List<VillageLocationType>
        {
            VillageLocationType.Forest,
            VillageLocationType.Mine,
            VillageLocationType.Smithy,
            VillageLocationType.Farm,
            VillageLocationType.Inn,
            VillageLocationType.Market
        };

        [SerializeField] private List<DestinationTypeRule> destinationTypeRules = new List<DestinationTypeRule>
        {
            new DestinationTypeRule { Type = VillageLocationType.Forest, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Mine, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Smithy, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Farm, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Inn, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Market, AllowMultiple = false, Weight = 1 },
            new DestinationTypeRule { Type = VillageLocationType.Well, AllowMultiple = true, Weight = 2 },
            new DestinationTypeRule { Type = VillageLocationType.House, AllowMultiple = true, Weight = 8 },
            new DestinationTypeRule { Type = VillageLocationType.Lumberyard, AllowMultiple = true, Weight = 2 }
        };

        [Header("Debug")]
        [SerializeField] private bool logRetries = true;
        [SerializeField] private bool drawSocketGizmos;
        [SerializeField, Tooltip("Last seed that was used during Generate Village.")] private int currentSeed;
        [SerializeField] private int retriesUsed;
        [SerializeField] private int contradictionCount;
        [SerializeField] private int connectedPathCellCount;
        [SerializeField] private bool allRequiredDestinationsReachable;
        [SerializeField] private List<VillageDestination> generatedDestinations = new List<VillageDestination>();

        private WFCPathTileDefinition[,] generatedGrid;

        [ContextMenu("Generate Village")]
        public void GenerateVillage()
        {
            var tileDefinitions = pathLibrary != null ? pathLibrary.GetUsableTileDefinitions() : null;
            if (tileDefinitions == null || tileDefinitions.Count == 0)
            {
                Debug.LogError("VillageWFCGenerator: No usable path tile definitions are configured.", this);
                return;
            }

            if (minimumPathEnds > maximumPathEnds)
            {
                Debug.LogError("VillageWFCGenerator: minimumPathEnds is greater than maximumPathEnds.", this);
                return;
            }

            currentSeed = useRandomSeed ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : seed;
            retriesUsed = 0;
            contradictionCount = 0;

            for (var attempt = 0; attempt < maxRetries; attempt++)
            {
                var attemptSeed = currentSeed + attempt;
                var random = new System.Random(attemptSeed);

                if (TryGenerateMap(tileDefinitions, random, out var grid, out var destinations, out var connectedCount, out var allConnected, out var failureReason))
                {
                    retriesUsed = attempt;
                    connectedPathCellCount = connectedCount;
                    allRequiredDestinationsReachable = allConnected;
                    generatedGrid = grid;
                    generatedDestinations = destinations;

                    RenderToTilemap();
                    if (villagePathGraph != null)
                    {
                        villagePathGraph.Build(generatedGrid, mapWidth, mapHeight, generatedDestinations, requiredDestinationTypes);
                    }

                    Debug.Log($"VillageWFCGenerator: generated map using seed {attemptSeed} after {attempt} retries.", this);
                    return;
                }

                if (failureReason == "contradiction")
                {
                    contradictionCount++;
                }

                if (logRetries)
                {
                    Debug.LogWarning($"VillageWFCGenerator: retry {attempt + 1}/{maxRetries} failed ({failureReason}).", this);
                }
            }

            Debug.LogError($"VillageWFCGenerator: failed to generate map after {maxRetries} retries (seed {currentSeed}).", this);
        }

        [ContextMenu("Clear Village")]
        public void ClearVillage()
        {
            generatedGrid = null;
            generatedDestinations.Clear();
            connectedPathCellCount = 0;
            allRequiredDestinationsReachable = false;

            if (targetTilemap != null)
            {
                targetTilemap.ClearAllTiles();
            }

            if (villagePathGraph != null)
            {
                villagePathGraph.Build(null, 0, 0, new List<VillageDestination>(), requiredDestinationTypes);
            }
        }

        public VillageDestination GetDestination(VillageLocationType type)
        {
            if (villagePathGraph == null)
            {
                return null;
            }

            return villagePathGraph.GetDestination(type);
        }

        public List<Vector3Int> FindPathToDestination(Vector3Int start, VillageLocationType destinationType)
        {
            if (villagePathGraph == null)
            {
                return new List<Vector3Int>();
            }

            return villagePathGraph.FindPathToDestination(start, destinationType);
        }

        private bool TryGenerateMap(
            List<WFCPathTileDefinition> tileDefinitions,
            System.Random random,
            out WFCPathTileDefinition[,] resultGrid,
            out List<VillageDestination> resultDestinations,
            out int resultConnectedPathCount,
            out bool resultAllRequiredConnected,
            out string failureReason)
        {
            resultGrid = null;
            resultDestinations = new List<VillageDestination>();
            resultConnectedPathCount = 0;
            resultAllRequiredConnected = false;
            failureReason = "unknown";

            var allTileIndices = new List<int>();
            for (var i = 0; i < tileDefinitions.Count; i++)
            {
                allTileIndices.Add(i);
            }

            var cells = new WFCGridCell[mapWidth, mapHeight];
            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    var candidates = new List<int>();
                    foreach (var tileIndex in allTileIndices)
                    {
                        var tile = tileDefinitions[tileIndex];
                        if (IsAllowedByMapBorder(tile, x, y))
                        {
                            candidates.Add(tileIndex);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        failureReason = "contradiction";
                        return false;
                    }

                    cells[x, y] = new WFCGridCell(candidates);
                }
            }

            var propagationQueue = new Queue<Vector2Int>();
            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    propagationQueue.Enqueue(new Vector2Int(x, y));
                }
            }

            if (!Propagate(cells, tileDefinitions, propagationQueue))
            {
                failureReason = "contradiction";
                return false;
            }

            while (true)
            {
                if (!TryGetLowestEntropyCell(cells, random, out var collapseX, out var collapseY))
                {
                    break;
                }

                var selectedTileIndex = PickWeightedTileIndex(cells[collapseX, collapseY], tileDefinitions, random);
                if (selectedTileIndex < 0 || !cells[collapseX, collapseY].CollapseTo(selectedTileIndex))
                {
                    failureReason = "contradiction";
                    return false;
                }

                propagationQueue.Clear();
                propagationQueue.Enqueue(new Vector2Int(collapseX, collapseY));
                if (!Propagate(cells, tileDefinitions, propagationQueue))
                {
                    failureReason = "contradiction";
                    return false;
                }
            }

            var resolvedGrid = new WFCPathTileDefinition[mapWidth, mapHeight];
            var pathEndPositions = new List<Vector3Int>();

            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    var collapsedIndex = cells[x, y].GetCollapsedTileIndex();
                    var selected = tileDefinitions[collapsedIndex];
                    resolvedGrid[x, y] = selected;

                    if (selected.IsPathEnd)
                    {
                        pathEndPositions.Add(new Vector3Int(x, y, 0));
                    }
                }
            }

            if (pathEndPositions.Count < minimumPathEnds || pathEndPositions.Count > maximumPathEnds)
            {
                failureReason = "path_end_count";
                return false;
            }

            if (!AssignDestinations(resolvedGrid, pathEndPositions, random, out var destinations))
            {
                failureReason = "destination_assignment";
                return false;
            }

            var allRequiredConnected = VillagePathGraph.AreRequiredDestinationsConnected(
                resolvedGrid,
                mapWidth,
                mapHeight,
                destinations,
                requiredDestinationTypes);
            if (!allRequiredConnected)
            {
                failureReason = "destination_connectivity";
                return false;
            }

            resultConnectedPathCount = VillagePathGraph.CountLargestConnectedPathComponent(resolvedGrid, mapWidth, mapHeight);
            resultAllRequiredConnected = allRequiredConnected;
            resultGrid = resolvedGrid;
            resultDestinations = destinations;
            failureReason = string.Empty;
            return true;
        }

        private bool AssignDestinations(
            WFCPathTileDefinition[,] grid,
            List<Vector3Int> pathEndPositions,
            System.Random random,
            out List<VillageDestination> destinations)
        {
            destinations = new List<VillageDestination>();
            if (pathEndPositions.Count == 0)
            {
                return true;
            }

            Shuffle(pathEndPositions, random);

            var usedUniqueTypes = new HashSet<VillageLocationType>();
            var destinationRulesByType = new Dictionary<VillageLocationType, DestinationTypeRule>();
            foreach (var rule in destinationTypeRules)
            {
                if (rule != null && rule.Type != VillageLocationType.None)
                {
                    destinationRulesByType[rule.Type] = rule;
                }
            }

            var pathEndCursor = 0;
            var uniqueRequiredTypes = new HashSet<VillageLocationType>();
            foreach (var requiredType in requiredDestinationTypes)
            {
                if (requiredType == VillageLocationType.None || !uniqueRequiredTypes.Add(requiredType))
                {
                    continue;
                }

                if (pathEndCursor >= pathEndPositions.Count)
                {
                    return false;
                }

                if (!destinationRulesByType.TryGetValue(requiredType, out var requiredRule))
                {
                    requiredRule = new DestinationTypeRule { Type = requiredType, AllowMultiple = false, Weight = 1 };
                    destinationRulesByType[requiredType] = requiredRule;
                }

                var position = pathEndPositions[pathEndCursor++];
                var tile = grid[position.x, position.y];
                destinations.Add(new VillageDestination
                {
                    LocationType = requiredType,
                    GridPosition = position,
                    EntranceDirection = tile.InferSingleSocketDirection()
                });

                if (!requiredRule.AllowMultiple)
                {
                    usedUniqueTypes.Add(requiredType);
                }
            }

            while (pathEndCursor < pathEndPositions.Count)
            {
                var selectedType = PickDestinationType(destinationRulesByType, usedUniqueTypes, random);
                if (selectedType == VillageLocationType.None)
                {
                    return false;
                }

                var position = pathEndPositions[pathEndCursor++];
                var tile = grid[position.x, position.y];
                destinations.Add(new VillageDestination
                {
                    LocationType = selectedType,
                    GridPosition = position,
                    EntranceDirection = tile.InferSingleSocketDirection()
                });

                if (destinationRulesByType.TryGetValue(selectedType, out var rule) && !rule.AllowMultiple)
                {
                    usedUniqueTypes.Add(selectedType);
                }
            }

            return true;
        }

        private VillageLocationType PickDestinationType(
            Dictionary<VillageLocationType, DestinationTypeRule> destinationRulesByType,
            HashSet<VillageLocationType> usedUniqueTypes,
            System.Random random)
        {
            var weightedPool = new List<(VillageLocationType type, int weight)>();
            foreach (var pair in destinationRulesByType)
            {
                var type = pair.Key;
                var rule = pair.Value;
                if (rule == null || type == VillageLocationType.None)
                {
                    continue;
                }

                if (!rule.AllowMultiple && usedUniqueTypes.Contains(type))
                {
                    continue;
                }

                weightedPool.Add((type, Mathf.Max(1, rule.Weight)));
            }

            if (weightedPool.Count == 0)
            {
                return VillageLocationType.None;
            }

            var totalWeight = 0;
            foreach (var item in weightedPool)
            {
                totalWeight += item.weight;
            }

            var roll = random.Next(0, totalWeight);
            var cursor = 0;
            foreach (var item in weightedPool)
            {
                cursor += item.weight;
                if (roll < cursor)
                {
                    return item.type;
                }
            }

            return weightedPool[weightedPool.Count - 1].type;
        }

        private bool Propagate(WFCGridCell[,] cells, List<WFCPathTileDefinition> definitions, Queue<Vector2Int> queue)
        {
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var currentCell = cells[current.x, current.y];

                foreach (var direction in EnumerateDirections())
                {
                    var offset = PathSocketDirectionUtility.ToOffset(direction);
                    var neighborX = current.x + offset.x;
                    var neighborY = current.y + offset.y;

                    if (neighborX < 0 || neighborX >= mapWidth || neighborY < 0 || neighborY >= mapHeight)
                    {
                        continue;
                    }

                    var neighborCell = cells[neighborX, neighborY];
                    var changed = neighborCell.RemoveWhere(neighborIndex =>
                        !AnyCompatible(currentCell, neighborIndex, definitions, direction));

                    if (neighborCell.Entropy == 0)
                    {
                        return false;
                    }

                    if (changed)
                    {
                        queue.Enqueue(new Vector2Int(neighborX, neighborY));
                    }
                }
            }

            return true;
        }

        private bool AnyCompatible(
            WFCGridCell currentCell,
            int neighborTileIndex,
            List<WFCPathTileDefinition> definitions,
            PathSocketDirection directionFromCurrentToNeighbor)
        {
            var neighbor = definitions[neighborTileIndex];
            foreach (var currentIndex in currentCell.PossibleTileIndices)
            {
                var current = definitions[currentIndex];
                if (VillagePathGraph.AreTilesCompatible(current, neighbor, directionFromCurrentToNeighbor))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetLowestEntropyCell(WFCGridCell[,] cells, System.Random random, out int resultX, out int resultY)
        {
            resultX = -1;
            resultY = -1;

            var lowestEntropy = int.MaxValue;
            var candidates = new List<Vector2Int>();

            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    var entropy = cells[x, y].Entropy;
                    if (entropy <= 1)
                    {
                        continue;
                    }

                    if (entropy < lowestEntropy)
                    {
                        lowestEntropy = entropy;
                        candidates.Clear();
                        candidates.Add(new Vector2Int(x, y));
                    }
                    else if (entropy == lowestEntropy)
                    {
                        candidates.Add(new Vector2Int(x, y));
                    }
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            var selected = candidates[random.Next(0, candidates.Count)];
            resultX = selected.x;
            resultY = selected.y;
            return true;
        }

        private int PickWeightedTileIndex(WFCGridCell cell, List<WFCPathTileDefinition> definitions, System.Random random)
        {
            var weightedTiles = new List<(int index, float weight)>();
            var totalWeight = 0f;

            foreach (var tileIndex in cell.PossibleTileIndices)
            {
                var weight = Mathf.Max(0.0001f, definitions[tileIndex].Weight);
                weightedTiles.Add((tileIndex, weight));
                totalWeight += weight;
            }

            if (weightedTiles.Count == 0)
            {
                return -1;
            }

            var roll = (float)random.NextDouble() * totalWeight;
            var cursor = 0f;
            foreach (var item in weightedTiles)
            {
                cursor += item.weight;
                if (roll <= cursor)
                {
                    return item.index;
                }
            }

            return weightedTiles[weightedTiles.Count - 1].index;
        }

        private bool IsAllowedByMapBorder(WFCPathTileDefinition tile, int x, int y)
        {
            if (tile == null)
            {
                return false;
            }

            if (tile.IsBlock)
            {
                return true;
            }

            if (x == 0 && tile.HasConnection(PathSocketDirection.Left))
            {
                return false;
            }

            if (x == mapWidth - 1 && tile.HasConnection(PathSocketDirection.Right))
            {
                return false;
            }

            if (y == 0 && tile.HasConnection(PathSocketDirection.Bottom))
            {
                return false;
            }

            if (y == mapHeight - 1 && tile.HasConnection(PathSocketDirection.Top))
            {
                return false;
            }

            return true;
        }

        private void RenderToTilemap()
        {
            if (targetTilemap == null)
            {
                return;
            }

            targetTilemap.ClearAllTiles();

            if (generatedGrid == null)
            {
                return;
            }

            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    var tile = generatedGrid[x, y];
                    var tileBase = tile != null ? tile.GetTileBase() : null;
                    if (tileBase != null)
                    {
                        targetTilemap.SetTile(new Vector3Int(x, y, 0), tileBase);
                    }
                }
            }
        }

        private static void Shuffle<T>(IList<T> items, System.Random random)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = random.Next(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        private static IEnumerable<PathSocketDirection> EnumerateDirections()
        {
            yield return PathSocketDirection.Top;
            yield return PathSocketDirection.Bottom;
            yield return PathSocketDirection.Left;
            yield return PathSocketDirection.Right;
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawSocketGizmos || generatedGrid == null)
            {
                return;
            }

            var cellSize = targetTilemap != null ? targetTilemap.cellSize : Vector3.one;
            for (var y = 0; y < mapHeight; y++)
            {
                for (var x = 0; x < mapWidth; x++)
                {
                    var tile = generatedGrid[x, y];
                    if (tile == null)
                    {
                        continue;
                    }

                    var center = targetTilemap != null
                        ? targetTilemap.GetCellCenterWorld(new Vector3Int(x, y, 0))
                        : transform.TransformPoint(new Vector3(x * cellSize.x, y * cellSize.y, 0f));

                    var halfX = cellSize.x * 0.5f;
                    var halfY = cellSize.y * 0.5f;

                    if (tile.HasConnection(PathSocketDirection.Top))
                    {
                        Gizmos.color = Color.green;
                        Gizmos.DrawLine(center, center + new Vector3(0f, halfY, 0f));
                    }

                    if (tile.HasConnection(PathSocketDirection.Bottom))
                    {
                        Gizmos.color = Color.blue;
                        Gizmos.DrawLine(center, center + new Vector3(0f, -halfY, 0f));
                    }

                    if (tile.HasConnection(PathSocketDirection.Left))
                    {
                        Gizmos.color = Color.yellow;
                        Gizmos.DrawLine(center, center + new Vector3(-halfX, 0f, 0f));
                    }

                    if (tile.HasConnection(PathSocketDirection.Right))
                    {
                        Gizmos.color = Color.red;
                        Gizmos.DrawLine(center, center + new Vector3(halfX, 0f, 0f));
                    }

                    if (tile.IsPathEnd)
                    {
                        Gizmos.color = Color.magenta;
                        Gizmos.DrawSphere(center, Mathf.Min(cellSize.x, cellSize.y) * 0.12f);
                    }
                }
            }
        }
    }
}
