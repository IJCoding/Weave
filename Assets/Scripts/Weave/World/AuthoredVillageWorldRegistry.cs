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
        private const float MinimumDurationSeconds = 0.01f;

        [SerializeField] private float pathGridCellSize = 0.5f;
        [SerializeField] private float pathBoundsPadding = 2f;

        private readonly List<AuthoredVillageLocation> locations = new List<AuthoredVillageLocation>();
        private readonly List<AuthoredVillageRoadTile> roadTiles = new List<AuthoredVillageRoadTile>();
        private readonly List<AuthoredVillageNpc> npcs = new List<AuthoredVillageNpc>();
        private readonly Dictionary<string, AuthoredVillageLocation> locationsById = new Dictionary<string, AuthoredVillageLocation>();
        private readonly Dictionary<string, AuthoredVillageNpc> npcsById = new Dictionary<string, AuthoredVillageNpc>();
        private readonly Dictionary<string, LocationDefinition> runtimeLocationsById = new Dictionary<string, LocationDefinition>();
        private readonly List<LocationDefinition> runtimeLocations = new List<LocationDefinition>();

        public IReadOnlyList<AuthoredVillageLocation> Locations => locations;
        public IReadOnlyList<AuthoredVillageRoadTile> RoadTiles => roadTiles;
        public IReadOnlyList<AuthoredVillageNpc> Npcs => npcs;
        public IReadOnlyDictionary<string, AuthoredVillageLocation> LocationsById => locationsById;
        public IReadOnlyDictionary<string, AuthoredVillageNpc> NpcsById => npcsById;

        private void Awake()
        {
            RefreshWorld();
        }

        private void OnValidate()
        {
            RefreshWorld();
            ValidateUniqueLocationIds();
            ValidateUniqueNpcIds();
        }

        public void RefreshWorld()
        {
            locations.Clear();
            roadTiles.Clear();
            npcs.Clear();
            locationsById.Clear();
            npcsById.Clear();

            GetComponentsInChildren(true, locations);
            GetComponentsInChildren(true, roadTiles);
            GetComponentsInChildren(true, npcs);

            foreach (var location in locations)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.LocationId))
                {
                    continue;
                }

                if (!locationsById.ContainsKey(location.LocationId))
                {
                    locationsById.Add(location.LocationId, location);
                }
            }

            foreach (var npc in npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.CharacterId))
                {
                    continue;
                }

                if (!npcsById.ContainsKey(npc.CharacterId))
                {
                    npcsById.Add(npc.CharacterId, npc);
                }
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

                foreach (var task in location.AvailableTasks)
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
                if (npc == null || string.IsNullOrWhiteSpace(npc.CharacterId) || !runState.Characters.TryGetValue(npc.CharacterId, out var state))
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

            foreach (var task in location.AvailableTasks)
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
                if (npc == null || !npc.InteractionAvailable || string.IsNullOrWhiteSpace(npc.CharacterId))
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
            var origin = originLocation != null ? originLocation.TravelAnchorPosition : Vector2.zero;
            var destination = destinationLocation != null ? destinationLocation.TravelAnchorPosition : origin;

            if (originLocationId == destinationLocationId || Vector2.Distance(origin, destination) <= 0.001f)
            {
                return new TravelPlan(
                    new List<Vector2> { destination },
                    new List<float> { Mathf.Max(sameLocationPreparationSeconds, MinimumDurationSeconds) },
                    Mathf.Max(sameLocationPreparationSeconds, MinimumDurationSeconds));
            }

            var rawPath = FindPath(origin, destination, Mathf.Max(pathGridCellSize, 0.25f), secondsPerDistanceUnit);
            if (rawPath.Count == 0)
            {
                rawPath.Add(origin);
                rawPath.Add(destination);
            }

            if (rawPath[0] != origin)
            {
                rawPath.Insert(0, origin);
            }

            if (rawPath[rawPath.Count - 1] != destination)
            {
                rawPath.Add(destination);
            }

            var simplified = SimplifyPath(rawPath);
            var cumulative = new List<float>(simplified.Count);
            var totalDuration = 0f;

            for (var index = 0; index < simplified.Count; index++)
            {
                if (index == 0)
                {
                    cumulative.Add(0f);
                    continue;
                }

                var previous = simplified[index - 1];
                var current = simplified[index];
                var segmentDistance = Vector2.Distance(previous, current);
                var terrainMultiplier = GetMovementMultiplierAt((previous + current) * 0.5f);
                var segmentDuration = segmentDistance * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds) * Mathf.Max(carryPenaltyMultiplier, 1f) / Mathf.Max(terrainMultiplier, 1f);
                totalDuration += Mathf.Max(segmentDuration, MinimumDurationSeconds);
                cumulative.Add(totalDuration);
            }

            return new TravelPlan(simplified, cumulative, Mathf.Max(totalDuration, MinimumDurationSeconds));
        }

        public float GetMovementMultiplierAt(Vector2 worldPoint)
        {
            var best = 1f;
            foreach (var roadTile in roadTiles)
            {
                if (roadTile != null && roadTile.Contains(worldPoint))
                {
                    best = Mathf.Max(best, roadTile.MovementMultiplier);
                }
            }

            return best;
        }

        public void ValidateUniqueLocationIds()
        {
            var seen = new Dictionary<string, AuthoredVillageLocation>();
            foreach (var location in locations)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.LocationId))
                {
                    continue;
                }

                if (seen.TryGetValue(location.LocationId, out var existing))
                {
                    Debug.LogError($"Duplicate Location ID '{location.LocationId}' on '{location.name}' and '{existing.name}'.", this);
                    continue;
                }

                seen.Add(location.LocationId, location);
            }
        }

        public void ValidateUniqueNpcIds()
        {
            var seen = new Dictionary<string, AuthoredVillageNpc>();
            foreach (var npc in npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.CharacterId))
                {
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

            foreach (var location in locations)
            {
                if (location == null || string.IsNullOrWhiteSpace(location.LocationId))
                {
                    continue;
                }

                var runtimeDefinition = location.CreateRuntimeDefinition();
                runtimeLocations.Add(runtimeDefinition);
                runtimeLocationsById[location.LocationId] = runtimeDefinition;
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
            SerializedFieldUtility.SetPrivateField(task, "requiredLocation", npc.HomeLocation != null ? npc.HomeLocation.LocationDefinition : null);
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

        private List<Vector2> FindPath(Vector2 origin, Vector2 destination, float cellSize, float secondsPerDistanceUnit)
        {
            var bounds = GetTraversalBounds(origin, destination);
            var width = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / cellSize) + 1);
            var height = Mathf.Max(2, Mathf.CeilToInt(bounds.size.y / cellSize) + 1);
            if (width * height > 40000)
            {
                return new List<Vector2> { origin, destination };
            }

            var originIndex = WorldToGrid(origin, bounds.min, cellSize);
            var destinationIndex = WorldToGrid(destination, bounds.min, cellSize);
            var frontier = new List<Vector2Int> { originIndex };
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, float> { [originIndex] = 0f };
            var fScore = new Dictionary<Vector2Int, float> { [originIndex] = Heuristic(originIndex, destinationIndex, cellSize, secondsPerDistanceUnit) };
            var closed = new HashSet<Vector2Int>();

            while (frontier.Count > 0)
            {
                frontier.Sort((left, right) => fScore[left].CompareTo(fScore[right]));
                var current = frontier[0];
                frontier.RemoveAt(0);

                if (current == destinationIndex)
                {
                    return ReconstructPath(cameFrom, current, bounds.min, cellSize, origin, destination);
                }

                closed.Add(current);

                foreach (var neighbor in EnumerateNeighbors(current, width, height))
                {
                    if (closed.Contains(neighbor))
                    {
                        continue;
                    }

                    var currentPoint = GridToWorld(current, bounds.min, cellSize);
                    var neighborPoint = GridToWorld(neighbor, bounds.min, cellSize);
                    var segmentDistance = Vector2.Distance(currentPoint, neighborPoint);
                    var multiplier = GetMovementMultiplierAt((currentPoint + neighborPoint) * 0.5f);
                    var tentative = gScore[current] + segmentDistance * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds) / Mathf.Max(multiplier, 1f);

                    if (gScore.TryGetValue(neighbor, out var existing) && tentative >= existing)
                    {
                        continue;
                    }

                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentative;
                    fScore[neighbor] = tentative + Heuristic(neighbor, destinationIndex, cellSize, secondsPerDistanceUnit);
                    if (!frontier.Contains(neighbor))
                    {
                        frontier.Add(neighbor);
                    }
                }
            }

            return new List<Vector2> { origin, destination };
        }

        private Bounds GetTraversalBounds(Vector2 origin, Vector2 destination)
        {
            var min = Vector2.Min(origin, destination);
            var max = Vector2.Max(origin, destination);

            foreach (var location in locations)
            {
                if (location == null)
                {
                    continue;
                }

                var bounds = location.GetBounds();
                min = Vector2.Min(min, bounds.min);
                max = Vector2.Max(max, bounds.max);
            }

            foreach (var roadTile in roadTiles)
            {
                if (roadTile == null)
                {
                    continue;
                }

                min = Vector2.Min(min, roadTile.Bounds.min);
                max = Vector2.Max(max, roadTile.Bounds.max);
            }

            var padding = Vector2.one * Mathf.Max(0.5f, pathBoundsPadding);
            min -= padding;
            max += padding;
            return new Bounds((min + max) * 0.5f, max - min);
        }

        private static Vector2Int WorldToGrid(Vector2 point, Vector2 min, float cellSize)
        {
            return new Vector2Int(
                Mathf.RoundToInt((point.x - min.x) / cellSize),
                Mathf.RoundToInt((point.y - min.y) / cellSize));
        }

        private static Vector2 GridToWorld(Vector2Int grid, Vector2 min, float cellSize)
        {
            return min + new Vector2(grid.x * cellSize, grid.y * cellSize);
        }

        private static float Heuristic(Vector2Int current, Vector2Int destination, float cellSize, float secondsPerDistanceUnit)
        {
            return Vector2Int.Distance(current, destination) * cellSize * Mathf.Max(secondsPerDistanceUnit, MinimumDurationSeconds);
        }

        private static IEnumerable<Vector2Int> EnumerateNeighbors(Vector2Int origin, int width, int height)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0)
                    {
                        continue;
                    }

                    var candidate = new Vector2Int(origin.x + dx, origin.y + dy);
                    if (candidate.x < 0 || candidate.y < 0 || candidate.x >= width || candidate.y >= height)
                    {
                        continue;
                    }

                    yield return candidate;
                }
            }
        }

        private static List<Vector2> ReconstructPath(
            Dictionary<Vector2Int, Vector2Int> cameFrom,
            Vector2Int current,
            Vector2 min,
            float cellSize,
            Vector2 origin,
            Vector2 destination)
        {
            var path = new List<Vector2> { destination };
            var cursor = current;
            path.Add(GridToWorld(cursor, min, cellSize));

            while (cameFrom.TryGetValue(cursor, out var previous))
            {
                cursor = previous;
                path.Add(GridToWorld(cursor, min, cellSize));
            }

            path.Add(origin);
            path.Reverse();
            return path;
        }

        private static List<Vector2> SimplifyPath(List<Vector2> path)
        {
            if (path.Count <= 2)
            {
                return path;
            }

            var simplified = new List<Vector2> { path[0] };
            var previousDirection = (path[1] - path[0]).normalized;

            for (var index = 1; index < path.Count - 1; index++)
            {
                var nextDirection = (path[index + 1] - path[index]).normalized;
                if (Vector2.Dot(previousDirection, nextDirection) < 0.999f)
                {
                    simplified.Add(path[index]);
                }

                previousDirection = nextDirection;
            }

            simplified.Add(path[path.Count - 1]);
            return simplified;
        }
    }
}
