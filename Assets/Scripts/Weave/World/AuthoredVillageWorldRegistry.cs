using System;
using System.Collections.Generic;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;

namespace Weave.World
{
    public readonly struct TravelPlan
    {
        public TravelPlan(List<Vector2> waypoints, List<float> cumulativeDurations, float totalDurationSeconds)
        {
            Waypoints = waypoints ?? new List<Vector2>();
            CumulativeDurations = cumulativeDurations ?? new List<float>();
            TotalDurationSeconds = Mathf.Max(totalDurationSeconds, 0.01f);
        }

        public IReadOnlyList<Vector2> Waypoints { get; }
        public IReadOnlyList<float> CumulativeDurations { get; }
        public float TotalDurationSeconds { get; }
    }

    public sealed class AuthoredVillageWorldRegistry : MonoBehaviour
    {
        [Serializable]
        private sealed class GeneratedLocationRule
        {
            public LocationDefinition Definition;
            public int Count = 1;
            public int MinimumSpacing = 0;
        }

        [Serializable]
        private sealed class PathNode
        {
            public Vector2Int Cell;
            public float Cost;

            public PathNode(Vector2Int cell, float cost)
            {
                Cell = cell;
                Cost = cost;
            }
        }

        private const float MinimumDurationSeconds = 0.01f;

        [Header("Shared Grid")]
        [SerializeField] private VillageGrid villageGrid;

        [Header("Build Mode")]
        [SerializeField] private VillageBuildMode buildMode = VillageBuildMode.Preset;
        [SerializeField] private int generationSeed = 12345;
        [SerializeField] private Vector2Int generationMin = new Vector2Int(-12, -12);
        [SerializeField] private Vector2Int generationMax = new Vector2Int(12, 12);
        [SerializeField] private List<GeneratedLocationRule> generatedLocationRules = new List<GeneratedLocationRule>();

        private readonly List<AuthoredVillageLocation> locations = new List<AuthoredVillageLocation>();
        private readonly List<AuthoredVillageRoadTile> roadTiles = new List<AuthoredVillageRoadTile>();
        private readonly List<AuthoredVillageNpc> npcs = new List<AuthoredVillageNpc>();
        private readonly Dictionary<string, AuthoredVillageLocation> locationsById = new Dictionary<string, AuthoredVillageLocation>();
        private readonly Dictionary<string, AuthoredVillageNpc> npcsById = new Dictionary<string, AuthoredVillageNpc>();
        private readonly Dictionary<Vector2Int, AuthoredVillageRoadTile> roadsByGridPosition = new Dictionary<Vector2Int, AuthoredVillageRoadTile>();
        private readonly Dictionary<Vector2Int, AuthoredVillageLocation> occupiedBuildingCells = new Dictionary<Vector2Int, AuthoredVillageLocation>();
        private readonly Dictionary<Vector2Int, AuthoredVillageNpc> npcsByGridPosition = new Dictionary<Vector2Int, AuthoredVillageNpc>();
        private readonly Dictionary<string, LocationDefinition> runtimeLocationsById = new Dictionary<string, LocationDefinition>();
        private readonly List<LocationDefinition> runtimeLocations = new List<LocationDefinition>();
        private readonly List<GameObject> generatedRuntimeObjects = new List<GameObject>();

        public VillageBuildMode BuildMode => buildMode;
        public VillageGrid SharedGrid => villageGrid != null ? villageGrid : Weave.World.VillageGrid.FindGrid(transform);
        public IReadOnlyList<AuthoredVillageLocation> Locations => locations;
        public IReadOnlyList<AuthoredVillageRoadTile> RoadTiles => roadTiles;
        public IReadOnlyList<AuthoredVillageNpc> Npcs => npcs;
        public IReadOnlyDictionary<string, AuthoredVillageLocation> LocationsByInstanceId => locationsById;
        public IReadOnlyDictionary<string, AuthoredVillageLocation> LocationsById => locationsById;
        public IReadOnlyDictionary<string, AuthoredVillageNpc> NpcsByCharacterId => npcsById;
        public IReadOnlyDictionary<string, AuthoredVillageNpc> NpcsById => npcsById;
        public IReadOnlyDictionary<Vector2Int, AuthoredVillageRoadTile> RoadsByGridPosition => roadsByGridPosition;

        private void Awake()
        {
            RefreshWorld();
        }

        private void OnValidate()
        {
            if (villageGrid == null)
            {
                villageGrid = Weave.World.VillageGrid.FindGrid(transform);
            }

            RefreshWorld();
            ValidateUniqueLocationIds();
            ValidateUniqueNpcIds();
        }

        public void RefreshWorld()
        {
            if (villageGrid == null)
            {
                villageGrid = Weave.World.VillageGrid.FindGrid(transform);
            }

            if (buildMode == VillageBuildMode.Generated)
            {
                RegenerateRuntimeLocations();
            }
            else
            {
                ClearGeneratedRuntimeObjects();
            }

            locations.Clear();
            roadTiles.Clear();
            npcs.Clear();
            locationsById.Clear();
            npcsById.Clear();
            roadsByGridPosition.Clear();
            occupiedBuildingCells.Clear();
            npcsByGridPosition.Clear();

            GetComponentsInChildren(true, locations);
            GetComponentsInChildren(true, roadTiles);
            GetComponentsInChildren(true, npcs);

            var duplicateLocationIds = new HashSet<string>();
            var duplicateNpcIds = new HashSet<string>();
            var seenLocationIds = new HashSet<string>();
            var seenNpcIds = new HashSet<string>();

            foreach (var location in locations)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.InstanceId))
                {
                    continue;
                }

                if (!seenLocationIds.Add(location.InstanceId))
                {
                    duplicateLocationIds.Add(location.InstanceId);
                    continue;
                }

                locationsById[location.InstanceId] = location;
                foreach (var cell in location.EnumerateFootprintCells())
                {
                    if (!occupiedBuildingCells.ContainsKey(cell))
                    {
                        occupiedBuildingCells[cell] = location;
                    }
                }
            }

            foreach (var roadTile in roadTiles)
            {
                if (roadTile == null)
                {
                    continue;
                }

                roadsByGridPosition[roadTile.GridPosition] = roadTile;
            }

            foreach (var npc in npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.CharacterId))
                {
                    continue;
                }

                if (!seenNpcIds.Add(npc.CharacterId))
                {
                    duplicateNpcIds.Add(npc.CharacterId);
                    continue;
                }

                npcsById[npc.CharacterId] = npc;
                npcsByGridPosition[npc.GridPosition] = npc;
            }

            foreach (var duplicateLocationId in duplicateLocationIds)
            {
                Debug.LogError($"Duplicate Location Instance ID '{duplicateLocationId}'.", this);
                locationsById.Remove(duplicateLocationId);
            }

            foreach (var duplicateNpcId in duplicateNpcIds)
            {
                Debug.LogError($"Duplicate Character ID '{duplicateNpcId}'.", this);
                npcsById.Remove(duplicateNpcId);
            }

            RebuildRuntimeLocations();
        }

        public List<LocationDefinition> BuildRuntimeLocations()
        {
            return new List<LocationDefinition>(runtimeLocations);
        }

        public List<CharacterDefinition> BuildCharacters(CharacterDefinition controlledCharacter)
        {
            var characters = new List<CharacterDefinition>();
            var seen = new HashSet<string>();

            if (controlledCharacter != null && !string.IsNullOrWhiteSpace(controlledCharacter.CharacterId))
            {
                seen.Add(controlledCharacter.CharacterId);
                characters.Add(controlledCharacter);
            }

            foreach (var npc in npcs)
            {
                var definition = npc != null ? npc.CharacterDefinition : null;
                if (definition == null || string.IsNullOrWhiteSpace(definition.CharacterId) || !seen.Add(definition.CharacterId))
                {
                    continue;
                }

                characters.Add(definition);
            }

            return characters;
        }

        public List<TaskDefinition> BuildStaticTasks()
        {
            var tasks = new List<TaskDefinition>();
            var seen = new HashSet<TaskDefinition>();

            foreach (var location in locations)
            {
                if (location == null)
                {
                    continue;
                }

                foreach (var task in location.GetAllTasks())
                {
                    if (task == null || !seen.Add(task))
                    {
                        continue;
                    }

                    tasks.Add(task);
                }
            }

            return tasks;
        }

        public void ApplyInitialCharacterPlacements(RunState runState, CharacterDefinition controlledCharacter)
        {
            if (runState == null)
            {
                return;
            }

            if (controlledCharacter != null && runState.Characters.TryGetValue(controlledCharacter.CharacterId, out var controlledState))
            {
                controlledState.HomeLocationId = ResolveDefinitionLocationId(controlledCharacter.HomeLocationId);
                controlledState.CurrentLocationId = controlledState.HomeLocationId;
                controlledState.TravelOriginLocationId = controlledState.CurrentLocationId;
                controlledState.TravelDestinationLocationId = string.Empty;
                controlledState.TravelRoute.Clear();
            }

            foreach (var npc in npcs)
            {
                if (npc == null ||
                    string.IsNullOrWhiteSpace(npc.CharacterId) ||
                    (controlledCharacter != null && npc.CharacterId == controlledCharacter.CharacterId) ||
                    !runState.Characters.TryGetValue(npc.CharacterId, out var state))
                {
                    continue;
                }

                var homeLocationId = npc.HomeLocation != null ? npc.HomeLocation.LocationId : ResolveDefinitionLocationId(npc.CharacterDefinition != null ? npc.CharacterDefinition.HomeLocationId : string.Empty);
                var startingLocationId = npc.StartingLocation != null ? npc.StartingLocation.LocationId : homeLocationId;
                state.HomeLocationId = homeLocationId;
                state.CurrentLocationId = startingLocationId;
                state.TravelOriginLocationId = startingLocationId;
                state.TravelDestinationLocationId = string.Empty;
                state.TravelRoute.Clear();
            }
        }

        public AuthoredVillageLocation FindLocation(string locationId)
        {
            RefreshIfNeeded();
            return !string.IsNullOrWhiteSpace(locationId) && locationsById.TryGetValue(locationId, out var location)
                ? location
                : null;
        }

        public AuthoredVillageNpc FindNpc(string characterId)
        {
            RefreshIfNeeded();
            return !string.IsNullOrWhiteSpace(characterId) && npcsById.TryGetValue(characterId, out var npc)
                ? npc
                : null;
        }

        public List<TaskDefinition> BuildCurrentLocationTasks(string locationId)
        {
            var boundTasks = new List<TaskDefinition>();
            var location = FindLocation(locationId);
            if (location == null)
            {
                return boundTasks;
            }

            foreach (var task in location.GetAllTasks())
            {
                if (task == null)
                {
                    continue;
                }

                boundTasks.Add(CreateBoundTask(task, location));
            }

            return boundTasks;
        }

        public List<TaskDefinition> BuildNpcInteractionTasks(string locationId, RunState runState)
        {
            var interactionTasks = new List<TaskDefinition>();
            if (runState == null || string.IsNullOrWhiteSpace(locationId))
            {
                return interactionTasks;
            }

            foreach (var npc in npcs)
            {
                if (npc == null ||
                    !npc.InteractionAvailable ||
                    string.IsNullOrWhiteSpace(npc.CharacterId) ||
                    npc.CharacterId == runState.ControlledCharacterId)
                {
                    continue;
                }

                if (!runState.Characters.TryGetValue(npc.CharacterId, out var npcState) || npcState.CurrentLocationId != locationId)
                {
                    continue;
                }

                interactionTasks.Add(CreateTalkTask(npc, locationId));
            }

            return interactionTasks;
        }

        public TravelPlan BuildTravelPlan(
            string originLocationId,
            string destinationLocationId,
            float secondsPerDistanceUnit,
            float carryPenaltyMultiplier,
            float sameLocationPreparationSeconds)
        {
            RefreshIfNeeded();
            var originLocation = FindLocation(originLocationId);
            var destinationLocation = FindLocation(destinationLocationId);
            var grid = SharedGrid;

            if (grid == null || originLocation == null || destinationLocation == null)
            {
                var originFallback = originLocation != null ? originLocation.TravelAnchorPosition : Vector2.zero;
                var destinationFallback = destinationLocation != null ? destinationLocation.TravelAnchorPosition : originFallback;
                var durationFallback = Mathf.Max(Vector2.Distance(originFallback, destinationFallback) * secondsPerDistanceUnit * carryPenaltyMultiplier, MinimumDurationSeconds);
                return new TravelPlan(new List<Vector2> { originFallback, destinationFallback }, new List<float> { 0f, durationFallback }, durationFallback);
            }

            var originCell = originLocation.TravelGridPosition;
            var destinationCell = destinationLocation.TravelGridPosition;
            if (originCell == destinationCell)
            {
                var same = Mathf.Max(sameLocationPreparationSeconds, MinimumDurationSeconds);
                var point = grid.GridToWorld(destinationCell);
                return new TravelPlan(new List<Vector2> { point }, new List<float> { same }, same);
            }

            var cellPath = FindCellPath(originCell, destinationCell);
            if (cellPath.Count == 0)
            {
                cellPath.Add(originCell);
                cellPath.Add(destinationCell);
            }

            var waypoints = new List<Vector2>(cellPath.Count);
            foreach (var cell in cellPath)
            {
                waypoints.Add(grid.GridToWorld(cell));
            }

            var cumulative = new List<float>(waypoints.Count) { 0f };
            var totalDuration = 0f;
            for (var i = 1; i < cellPath.Count; i++)
            {
                var from = cellPath[i - 1];
                var to = cellPath[i];
                var terrainMultiplier = Mathf.Max(1f, GetRoadMovementMultiplierForSegment(from, to));
                var segmentDistance = Vector2.Distance(waypoints[i - 1], waypoints[i]);
                var segmentDuration = segmentDistance * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds) * Mathf.Max(carryPenaltyMultiplier, 1f) / terrainMultiplier;
                totalDuration += Mathf.Max(segmentDuration, MinimumDurationSeconds);
                cumulative.Add(totalDuration);
            }

            return new TravelPlan(waypoints, cumulative, Mathf.Max(totalDuration, MinimumDurationSeconds));
        }

        public float GetMovementMultiplierAt(Vector2 worldPoint)
        {
            var grid = SharedGrid;
            if (grid == null)
            {
                return 1f;
            }

            var cell = grid.WorldToGrid(worldPoint);
            if (roadsByGridPosition.TryGetValue(cell, out var road) && road != null)
            {
                return Mathf.Max(1f, road.MovementMultiplier);
            }

            return 1f;
        }

        public bool IsRoadCell(Vector2Int cell)
        {
            return roadsByGridPosition.ContainsKey(cell);
        }

        public bool IsBuildingCellBlocked(Vector2Int cell, Vector2Int origin, Vector2Int destination)
        {
            if (!occupiedBuildingCells.TryGetValue(cell, out var owner) || owner == null)
            {
                return false;
            }

            return cell != origin && cell != destination;
        }

        public void ValidateUniqueLocationIds()
        {
            var seen = new Dictionary<string, AuthoredVillageLocation>();
            foreach (var location in locations)
            {
                if (location == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(location.InstanceId))
                {
                    Debug.LogError($"Location '{location.name}' is missing an Instance ID.", location);
                    continue;
                }

                if (seen.TryGetValue(location.InstanceId, out var existing))
                {
                    Debug.LogError($"Duplicate Location Instance ID '{location.InstanceId}' on '{location.name}' and '{existing.name}'.", this);
                    continue;
                }

                seen.Add(location.InstanceId, location);
            }
        }

        public void ValidateUniqueNpcIds()
        {
            var seen = new Dictionary<string, AuthoredVillageNpc>();
            foreach (var npc in npcs)
            {
                if (npc == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(npc.CharacterId))
                {
                    Debug.LogError($"NPC '{npc.name}' is missing a Character ID via CharacterDefinition.", npc);
                    continue;
                }

                if (seen.TryGetValue(npc.CharacterId, out var existing))
                {
                    Debug.LogError($"Duplicate Character ID '{npc.CharacterId}' on '{npc.name}' and '{existing.name}'.", this);
                    continue;
                }

                seen.Add(npc.CharacterId, npc);
            }
        }

        private void RefreshIfNeeded()
        {
            if (locations.Count == 0 && roadTiles.Count == 0 && npcs.Count == 0)
            {
                RefreshWorld();
            }
        }

        private void RebuildRuntimeLocations()
        {
            runtimeLocations.Clear();
            runtimeLocationsById.Clear();

            foreach (var location in locationsById.Values)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.InstanceId))
                {
                    continue;
                }

                var runtimeDefinition = location.CreateRuntimeDefinition();
                runtimeLocations.Add(runtimeDefinition);
                runtimeLocationsById[location.InstanceId] = runtimeDefinition;
            }
        }

        private string ResolveDefinitionLocationId(string locationId)
        {
            return string.IsNullOrWhiteSpace(locationId) ? string.Empty : locationId;
        }

        private TaskDefinition CreateBoundTask(TaskDefinition source, AuthoredVillageLocation location)
        {
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            task.hideFlags = HideFlags.HideAndDontSave;
            SerializedFieldUtility.SetPrivateField(task, "taskId", $"location::{location.LocationId}::{source.TaskId}");
            SerializedFieldUtility.SetPrivateField(task, "displayName", source.DisplayName);
            SerializedFieldUtility.SetPrivateField(task, "requiredLocation", location.LocationDefinition);
            SerializedFieldUtility.SetPrivateField(task, "requiredLocationId", location.LocationId);
            SerializedFieldUtility.SetPrivateField(task, "eligibleCharacters", new List<CharacterDefinition>(source.EligibleCharacters));
            SerializedFieldUtility.SetPrivateField(task, "requiredWorldFlags", new List<string>(source.RequiredWorldFlags));
            SerializedFieldUtility.SetPrivateField(task, "blockedWorldFlags", new List<string>(source.BlockedWorldFlags));
            SerializedFieldUtility.SetPrivateField(task, "durationSeconds", source.DurationSeconds);
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", new List<ResourceAmount>(source.ActorResourceChanges));
            SerializedFieldUtility.SetPrivateField(task, "rewardsAddedToCarriedResources", source.RewardsAddedToCarriedResources);
            SerializedFieldUtility.SetPrivateField(task, "completeOnArrival", source.CompleteOnArrival);
            SerializedFieldUtility.SetPrivateField(task, "unavailableWhenAlreadyAtRequiredLocation", source.UnavailableWhenAlreadyAtRequiredLocation);
            SerializedFieldUtility.SetPrivateField(task, "followUpEvent", source.FollowUpEvent);
            return task;
        }

        private TaskDefinition CreateTalkTask(AuthoredVillageNpc npc, string locationId)
        {
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            task.hideFlags = HideFlags.HideAndDontSave;
            SerializedFieldUtility.SetPrivateField(task, "taskId", $"talk::{npc.CharacterId}::{locationId}");
            SerializedFieldUtility.SetPrivateField(task, "displayName", $"Talk to {npc.DisplayName}");
            SerializedFieldUtility.SetPrivateField(task, "requiredLocation", null);
            SerializedFieldUtility.SetPrivateField(task, "requiredLocationId", locationId);
            SerializedFieldUtility.SetPrivateField(task, "eligibleCharacters", new List<CharacterDefinition>());
            SerializedFieldUtility.SetPrivateField(task, "requiredWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "blockedWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "durationSeconds", npc.TalkDurationSeconds);
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", new List<ResourceAmount>());
            SerializedFieldUtility.SetPrivateField(task, "rewardsAddedToCarriedResources", false);
            SerializedFieldUtility.SetPrivateField(task, "completeOnArrival", false);
            SerializedFieldUtility.SetPrivateField(task, "unavailableWhenAlreadyAtRequiredLocation", false);
            SerializedFieldUtility.SetPrivateField(task, "followUpEvent", npc.TalkEvent);
            return task;
        }

        private void RegenerateRuntimeLocations()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ClearGeneratedRuntimeObjects();
            var grid = SharedGrid;
            if (grid == null)
            {
                return;
            }

            var random = new System.Random(generationSeed);
            var occupied = new HashSet<Vector2Int>();
            var existingLocations = new List<AuthoredVillageLocation>();
            GetComponentsInChildren(true, existingLocations);
            foreach (var existing in existingLocations)
            {
                if (existing == null || existing.PlacementSource != LocationPlacementSource.Preset)
                {
                    continue;
                }

                foreach (var cell in existing.EnumerateFootprintCells())
                {
                    occupied.Add(cell);
                }
            }

            var sequenceByDefinition = new Dictionary<string, int>();
            foreach (var rule in generatedLocationRules)
            {
                if (rule == null || rule.Definition == null || rule.Count <= 0)
                {
                    continue;
                }

                var baseId = string.IsNullOrWhiteSpace(rule.Definition.Id) ? rule.Definition.name.ToLowerInvariant() : rule.Definition.Id;
                if (!sequenceByDefinition.ContainsKey(baseId))
                {
                    sequenceByDefinition[baseId] = 0;
                }

                var footprintWidth = rule.Definition.FootprintWidth;
                var footprintHeight = rule.Definition.FootprintHeight;

                for (var i = 0; i < rule.Count; i++)
                {
                    if (!TryFindFreeCell(random, occupied, footprintWidth, footprintHeight, rule.MinimumSpacing, out var selectedCell))
                    {
                        break;
                    }

                    sequenceByDefinition[baseId]++;
                    var instanceId = $"{baseId}_{sequenceByDefinition[baseId]}";
                    var go = rule.Definition.BuildingPrefab != null
                        ? Instantiate(rule.Definition.BuildingPrefab, transform)
                        : new GameObject(rule.Definition.DisplayName);
                    go.name = rule.Definition.DisplayName;
                    var location = go.GetComponent<AuthoredVillageLocation>();
                    if (location == null)
                    {
                        location = go.AddComponent<AuthoredVillageLocation>();
                    }

                    location.ConfigureGenerated(instanceId, rule.Definition, selectedCell);
                    generatedRuntimeObjects.Add(go);
                    for (var x = 0; x < footprintWidth; x++)
                    {
                        for (var y = 0; y < footprintHeight; y++)
                        {
                            occupied.Add(selectedCell + new Vector2Int(x, y));
                        }
                    }
                }
            }
        }

        private void ClearGeneratedRuntimeObjects()
        {
            for (var i = 0; i < generatedRuntimeObjects.Count; i++)
            {
                var instance = generatedRuntimeObjects[i];
                if (instance == null)
                {
                    continue;
                }

                Destroy(instance);
            }

            generatedRuntimeObjects.Clear();
        }

        private bool TryFindFreeCell(System.Random random, HashSet<Vector2Int> occupied, int width, int height, int minimumSpacing, out Vector2Int selectedCell)
        {
            var candidates = new List<Vector2Int>();
            for (var x = generationMin.x; x <= generationMax.x; x++)
            {
                for (var y = generationMin.y; y <= generationMax.y; y++)
                {
                    var candidate = new Vector2Int(x, y);
                    if (CanPlaceFootprint(candidate, width, height, minimumSpacing, occupied))
                    {
                        candidates.Add(candidate);
                    }
                }
            }

            if (candidates.Count == 0)
            {
                selectedCell = default;
                return false;
            }

            selectedCell = candidates[random.Next(0, candidates.Count)];
            return true;
        }

        private bool CanPlaceFootprint(Vector2Int anchor, int width, int height, int minimumSpacing, HashSet<Vector2Int> occupied)
        {
            for (var x = -minimumSpacing; x < width + minimumSpacing; x++)
            {
                for (var y = -minimumSpacing; y < height + minimumSpacing; y++)
                {
                    var testCell = anchor + new Vector2Int(x, y);
                    if (occupied.Contains(testCell))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private List<Vector2Int> FindCellPath(Vector2Int origin, Vector2Int destination)
        {
            GetSearchBounds(origin, destination, out var min, out var max);
            var frontier = new List<PathNode> { new PathNode(origin, 0f) };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float> { [origin] = 0f };
            var closed = new HashSet<Vector2Int>();
            var safetyCounter = 0;

            while (frontier.Count > 0 && safetyCounter++ < 10000)
            {
                frontier.Sort((a, b) => a.Cost.CompareTo(b.Cost));
                var current = frontier[0].Cell;
                frontier.RemoveAt(0);
                if (current == destination)
                {
                    return ReconstructCellPath(cameFrom, current);
                }

                if (!closed.Add(current))
                {
                    continue;
                }

                foreach (var neighbor in EnumerateOrthogonalNeighbors(current))
                {
                    if (closed.Contains(neighbor) ||
                        neighbor.x < min.x ||
                        neighbor.y < min.y ||
                        neighbor.x > max.x ||
                        neighbor.y > max.y ||
                        IsBuildingCellBlocked(neighbor, origin, destination))
                    {
                        continue;
                    }

                    var roadMultiplier = Mathf.Max(1f, GetRoadMovementMultiplierForSegment(current, neighbor));
                    var stepCost = 1f / roadMultiplier;
                    var tentative = gScore[current] + stepCost;
                    if (gScore.TryGetValue(neighbor, out var existing) && tentative >= existing)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentative;
                    var priority = tentative + ManhattanDistance(neighbor, destination);
                    frontier.Add(new PathNode(neighbor, priority));
                }
            }

            return new List<Vector2Int>();
        }

        private void GetSearchBounds(Vector2Int origin, Vector2Int destination, out Vector2Int min, out Vector2Int max)
        {
            min = Vector2Int.Min(origin, destination);
            max = Vector2Int.Max(origin, destination);

            foreach (var location in locations)
            {
                if (location == null)
                {
                    continue;
                }

                foreach (var cell in location.EnumerateFootprintCells())
                {
                    min = Vector2Int.Min(min, cell);
                    max = Vector2Int.Max(max, cell);
                }

                min = Vector2Int.Min(min, location.TravelGridPosition);
                max = Vector2Int.Max(max, location.TravelGridPosition);
            }

            foreach (var roadCell in roadsByGridPosition.Keys)
            {
                min = Vector2Int.Min(min, roadCell);
                max = Vector2Int.Max(max, roadCell);
            }

            var padding = 6;
            min -= new Vector2Int(padding, padding);
            max += new Vector2Int(padding, padding);
        }

        private float GetRoadMovementMultiplierForSegment(Vector2Int from, Vector2Int to)
        {
            var best = 1f;
            if (roadsByGridPosition.TryGetValue(from, out var fromRoad) && fromRoad != null)
            {
                best = Mathf.Max(best, fromRoad.MovementMultiplier);
            }

            if (roadsByGridPosition.TryGetValue(to, out var toRoad) && toRoad != null)
            {
                best = Mathf.Max(best, toRoad.MovementMultiplier);
            }

            return best;
        }

        private static int ManhattanDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        }

        private static IEnumerable<Vector2Int> EnumerateOrthogonalNeighbors(Vector2Int origin)
        {
            yield return origin + Vector2Int.up;
            yield return origin + Vector2Int.down;
            yield return origin + Vector2Int.left;
            yield return origin + Vector2Int.right;
        }

        private static List<Vector2Int> ReconstructCellPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
        {
            var path = new List<Vector2Int> { current };
            while (cameFrom.TryGetValue(current, out var previous))
            {
                current = previous;
                path.Add(current);
            }

            path.Reverse();
            return path;
        }
    }
}
