using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Weave.Data;
using Weave.Presentation;
using Weave.Simulation;
using Weave.World;

namespace Weave.Editor
{
    public static class PrototypeVillageSceneSetupUtility
    {
        private const string WorldRootName = "VillageWorldRoot";
        private const string LocationsContainerName = "Locations";
        private const string RoadsContainerName = "Roads";
        private const string NpcsContainerName = "NPCs";

        [MenuItem("Weave/Scene Setup/Create Preset Prototype Village")]
        private static void CreatePresetVillage()
        {
            SetupScene(VillageBuildMode.Preset);
        }

        [MenuItem("Weave/Scene Setup/Create Generated Prototype Village")]
        private static void CreateGeneratedVillage()
        {
            SetupScene(VillageBuildMode.Generated);
        }

        private static void SetupScene(VillageBuildMode buildMode)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogError("Stop Play Mode before running scene setup.");
                return;
            }

            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                Debug.LogError("No active scene available for setup.");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Create {buildMode} Prototype Village");

            var worldRoot = GetOrCreateRootObject(WorldRootName, scene);
            var villageGrid = GetOrAddComponent<VillageGrid>(worldRoot);
            var worldRegistry = GetOrAddComponent<AuthoredVillageWorldRegistry>(worldRoot);
            RemoveDuplicateComponents(scene, villageGrid);
            RemoveDuplicateComponents(scene, worldRegistry);
            ConfigureRegistry(worldRegistry, villageGrid, buildMode);

            var locationsContainer = ResetContainer(worldRoot.transform, LocationsContainerName);
            var roadsContainer = ResetContainer(worldRoot.transform, RoadsContainerName);
            var npcsContainer = ResetContainer(worldRoot.transform, NpcsContainerName);

            var locationsById = CreateCoreLocations(locationsContainer);
            CreateRoadConnectivity(roadsContainer);
            CreateNpcs(npcsContainer, locationsById);

            var bootstrap = GetOrCreateRootObject("PrototypeBootstrap", scene);
            var session = GetOrAddComponent<PrototypeGameSession>(bootstrap);
            var scenario = GetOrAddComponent<AuthoredVillageScenario>(bootstrap);
            GetOrAddComponent<PrototypeShowcaseController>(bootstrap);

            ConfigureScenario(scenario, worldRegistry);
            ConfigureSession(session, worldRegistry);

            worldRegistry.RefreshWorld();
            worldRegistry.ValidateUniqueLocationIds();
            worldRegistry.ValidateUniqueNpcIds();

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = bootstrap;
            Debug.Log($"Prototype village setup complete in {buildMode} mode.", bootstrap);
        }

        private static Dictionary<string, AuthoredVillageLocation> CreateCoreLocations(Transform parent)
        {
            var locations = new Dictionary<string, AuthoredVillageLocation>();

            locations["home"] = CreateLocation(
                "Home",
                "Assets/Prefabs/World/Locations/House.prefab",
                "home",
                new Vector2Int(0, 0),
                parent,
                true);

            locations["mine"] = CreateLocation(
                "Mine",
                "Assets/Prefabs/World/Locations/Mine.prefab",
                "mine",
                new Vector2Int(4, 0),
                parent,
                false);

            locations["smithy"] = CreateLocation(
                "Smithy",
                "Assets/Prefabs/World/Locations/Smithy.prefab",
                "smithy",
                new Vector2Int(0, 4),
                parent,
                false);

            locations["forest"] = CreateLocation(
                "Forest",
                "Assets/Prefabs/World/Locations/Forest.prefab",
                "forest",
                new Vector2Int(4, 4),
                parent,
                false);

            return locations;
        }

        private static void CreateRoadConnectivity(Transform parent)
        {
            var roadPrefab = LoadAsset<GameObject>(
                "Assets/Prefabs/World/Roads/RoadTile.prefab",
                "Assets/Prefabs/World/RoadTile.prefab");
            if (roadPrefab == null)
            {
                return;
            }

            var roadCells = new HashSet<Vector2Int>();
            AddLine(roadCells, new Vector2Int(0, -1), new Vector2Int(4, -1));
            AddLine(roadCells, new Vector2Int(0, 3), new Vector2Int(4, 3));
            AddLine(roadCells, new Vector2Int(0, -1), new Vector2Int(0, 3));
            AddLine(roadCells, new Vector2Int(4, -1), new Vector2Int(4, 3));

            foreach (var cell in roadCells)
            {
                var roadObject = (GameObject)PrefabUtility.InstantiatePrefab(roadPrefab, parent);
                Undo.RegisterCreatedObjectUndo(roadObject, "Create Road Tile");
                roadObject.name = $"Road_{cell.x}_{cell.y}";
                var road = roadObject.GetComponent<AuthoredVillageRoadTile>();
                if (road != null)
                {
                    road.SetGridPosition(cell);
                    EditorUtility.SetDirty(road);
                }
            }
        }

        private static void CreateNpcs(Transform parent, IReadOnlyDictionary<string, AuthoredVillageLocation> locationsById)
        {
            var npcPrefab = LoadAsset<GameObject>(
                "Assets/Prefabs/World/NPCs/NPC.prefab",
                "Assets/Prefabs/World/NPC.prefab");
            if (npcPrefab == null)
            {
                return;
            }

            CreateNpc(
                "Mina",
                npcPrefab,
                parent,
                "Assets/Data/Prototype/Mina.asset",
                locationsById["mine"],
                locationsById["mine"],
                "Assets/Data/Prototype/TalkToMina.asset");

            CreateNpc(
                "Rowan",
                npcPrefab,
                parent,
                "Assets/Data/Prototype/Rowan.asset",
                locationsById["smithy"],
                locationsById["smithy"],
                "Assets/Data/Prototype/TalkToRowan.asset");
        }

        private static AuthoredVillageLocation CreateLocation(
            string objectName,
            string prefabPath,
            string instanceId,
            Vector2Int gridPosition,
            Transform parent,
            bool isHome)
        {
            var prefab = LoadAsset<GameObject>(prefabPath);
            if (prefab == null)
            {
                return null;
            }

            var locationObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(locationObject, "Create Location");
            locationObject.name = objectName;

            var location = locationObject.GetComponent<AuthoredVillageLocation>();
            if (location == null)
            {
                return null;
            }

            location.SetGridPosition(gridPosition);

            var locationSerializedObject = new SerializedObject(location);
            var instanceIdProperty = RequireProperty(locationSerializedObject, "instanceId", nameof(AuthoredVillageLocation));
            var locationIdProperty = RequireProperty(locationSerializedObject, "locationId", nameof(AuthoredVillageLocation));
            var isHomeProperty = RequireProperty(locationSerializedObject, "isHome", nameof(AuthoredVillageLocation));
            if (instanceIdProperty == null || locationIdProperty == null || isHomeProperty == null)
            {
                return location;
            }

            instanceIdProperty.stringValue = instanceId;
            locationIdProperty.stringValue = instanceId;
            isHomeProperty.boolValue = isHome;
            locationSerializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(location);
            return location;
        }

        private static void CreateNpc(
            string objectName,
            GameObject npcPrefab,
            Transform parent,
            string characterPath,
            AuthoredVillageLocation startingLocation,
            AuthoredVillageLocation homeLocation,
            string talkEventPath)
        {
            var npcObject = (GameObject)PrefabUtility.InstantiatePrefab(npcPrefab, parent);
            Undo.RegisterCreatedObjectUndo(npcObject, "Create NPC");
            npcObject.name = objectName;

            var npc = npcObject.GetComponent<AuthoredVillageNpc>();
            if (npc == null)
            {
                return;
            }

            var characterDefinition = LoadAsset<CharacterDefinition>(characterPath);
            var talkEvent = LoadAsset<EventDefinition>(talkEventPath);

            var serializedNpc = new SerializedObject(npc);
            var characterProperty = RequireProperty(serializedNpc, "characterDefinition", nameof(AuthoredVillageNpc));
            var startingLocationProperty = RequireProperty(serializedNpc, "startingLocation", nameof(AuthoredVillageNpc));
            var homeLocationProperty = RequireProperty(serializedNpc, "homeLocation", nameof(AuthoredVillageNpc));
            var talkEventProperty = RequireProperty(serializedNpc, "talkEvent", nameof(AuthoredVillageNpc));
            if (characterProperty == null ||
                startingLocationProperty == null ||
                homeLocationProperty == null ||
                talkEventProperty == null)
            {
                return;
            }

            characterProperty.objectReferenceValue = characterDefinition;
            startingLocationProperty.objectReferenceValue = startingLocation;
            homeLocationProperty.objectReferenceValue = homeLocation;
            talkEventProperty.objectReferenceValue = talkEvent;
            serializedNpc.ApplyModifiedPropertiesWithoutUndo();

            npc.SetGridPosition(startingLocation != null ? startingLocation.GridPosition : Vector2Int.zero);
            EditorUtility.SetDirty(npc);
        }

        private static void ConfigureRegistry(AuthoredVillageWorldRegistry registry, VillageGrid villageGrid, VillageBuildMode buildMode)
        {
            var serializedRegistry = new SerializedObject(registry);
            var villageGridProperty = RequireProperty(serializedRegistry, "villageGrid", nameof(AuthoredVillageWorldRegistry));
            var buildModeProperty = RequireProperty(serializedRegistry, "buildMode", nameof(AuthoredVillageWorldRegistry));
            var generationSeedProperty = RequireProperty(serializedRegistry, "generationSeed", nameof(AuthoredVillageWorldRegistry));
            var generationMinProperty = RequireProperty(serializedRegistry, "generationMin", nameof(AuthoredVillageWorldRegistry));
            var generationMaxProperty = RequireProperty(serializedRegistry, "generationMax", nameof(AuthoredVillageWorldRegistry));
            if (villageGridProperty == null ||
                buildModeProperty == null ||
                generationSeedProperty == null ||
                generationMinProperty == null ||
                generationMaxProperty == null)
            {
                return;
            }

            villageGridProperty.objectReferenceValue = villageGrid;
            buildModeProperty.enumValueIndex = (int)buildMode;
            generationSeedProperty.intValue = 12345;
            generationMinProperty.vector2IntValue = new Vector2Int(-12, -12);
            generationMaxProperty.vector2IntValue = new Vector2Int(12, 12);
            ConfigureGeneratedRules(serializedRegistry, buildMode);
            serializedRegistry.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
        }

        private static void ConfigureGeneratedRules(SerializedObject serializedRegistry, VillageBuildMode buildMode)
        {
            var rules = serializedRegistry.FindProperty("generatedLocationRules");
            if (rules == null)
            {
                Debug.LogError("Missing serialized field 'generatedLocationRules' on AuthoredVillageWorldRegistry.");
                return;
            }

            rules.ClearArray();

            if (buildMode != VillageBuildMode.Generated)
            {
                return;
            }

            AddGeneratedRule(rules, "Assets/Data/Locations/Farm.asset", 2, 1);
            AddGeneratedRule(rules, "Assets/Data/Locations/Market.asset", 1, 2);
            AddGeneratedRule(rules, "Assets/Data/Locations/Lumberyard.asset", 1, 1);
            AddGeneratedRule(rules, "Assets/Data/Locations/Well.asset", 1, 1);
        }

        private static void AddGeneratedRule(SerializedProperty rules, string definitionPath, int count, int minimumSpacing)
        {
            var definition = LoadAsset<LocationDefinition>(definitionPath);
            if (definition == null)
            {
                return;
            }

            var index = rules.arraySize;
            rules.InsertArrayElementAtIndex(index);
            var element = rules.GetArrayElementAtIndex(index);
            var definitionProperty = RequireProperty(element, "Definition", "GeneratedLocationRule");
            var countProperty = RequireProperty(element, "Count", "GeneratedLocationRule");
            var spacingProperty = RequireProperty(element, "MinimumSpacing", "GeneratedLocationRule");
            if (definitionProperty == null || countProperty == null || spacingProperty == null)
            {
                return;
            }

            definitionProperty.objectReferenceValue = definition;
            countProperty.intValue = Mathf.Max(1, count);
            spacingProperty.intValue = Mathf.Max(0, minimumSpacing);
        }

        private static void ConfigureScenario(AuthoredVillageScenario scenario, AuthoredVillageWorldRegistry worldRegistry)
        {
            var serializedScenario = new SerializedObject(scenario);
            var calendarProperty = RequireProperty(serializedScenario, "calendarDefinition", nameof(AuthoredVillageScenario));
            var controlledCharacterProperty = RequireProperty(serializedScenario, "controlledCharacter", nameof(AuthoredVillageScenario));
            var worldRegistryProperty = RequireProperty(serializedScenario, "worldRegistry", nameof(AuthoredVillageScenario));
            var resourcesProperty = RequireProperty(serializedScenario, "resources", nameof(AuthoredVillageScenario));
            if (calendarProperty == null ||
                controlledCharacterProperty == null ||
                worldRegistryProperty == null ||
                resourcesProperty == null)
            {
                return;
            }

            calendarProperty.objectReferenceValue = LoadAsset<GameCalendarDefinition>("Assets/Data/Prototype/PrototypeCalendar.asset");
            controlledCharacterProperty.objectReferenceValue = LoadAsset<CharacterDefinition>("Assets/Data/Prototype/PlayerCharacter.asset");
            worldRegistryProperty.objectReferenceValue = worldRegistry;

            resourcesProperty.ClearArray();
            AddResource(resourcesProperty, "Assets/Data/Prototype/Goodwill.asset");
            AddResource(resourcesProperty, "Assets/Data/Prototype/Wood.asset");
            AddResource(resourcesProperty, "Assets/Data/Prototype/Grain.asset");
            AddResource(resourcesProperty, "Assets/Data/Prototype/Iron.asset");

            serializedScenario.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scenario);
        }

        private static void AddResource(SerializedProperty resourcesProperty, string assetPath)
        {
            var resource = LoadAsset<ResourceDefinition>(assetPath);
            if (resource == null)
            {
                return;
            }

            var index = resourcesProperty.arraySize;
            resourcesProperty.InsertArrayElementAtIndex(index);
            resourcesProperty.GetArrayElementAtIndex(index).objectReferenceValue = resource;
        }

        private static void ConfigureSession(PrototypeGameSession session, AuthoredVillageWorldRegistry worldRegistry)
        {
            var serializedSession = new SerializedObject(session);
            var authoredWorldProperty = RequireProperty(serializedSession, "authoredWorld", nameof(PrototypeGameSession));
            if (authoredWorldProperty == null)
            {
                return;
            }

            authoredWorldProperty.objectReferenceValue = worldRegistry;
            serializedSession.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(session);
        }

        private static Transform ResetContainer(Transform parent, string name)
        {
            var container = parent.Find(name);
            if (container == null)
            {
                var containerObject = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(containerObject, "Create Container");
                containerObject.transform.SetParent(parent, false);
                return containerObject.transform;
            }

            for (var i = container.childCount - 1; i >= 0; i--)
            {
                Undo.DestroyObjectImmediate(container.GetChild(i).gameObject);
            }

            return container;
        }

        private static GameObject GetOrCreateRootObject(string name, Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                {
                    return root;
                }
            }

            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create Root Object");
            SceneManager.MoveGameObjectToScene(created, scene);
            return created;
        }

        private static T GetOrAddComponent<T>(GameObject gameObject) where T : Component
        {
            var existing = gameObject.GetComponent<T>();
            if (existing != null)
            {
                return existing;
            }

            return Undo.AddComponent<T>(gameObject);
        }

        private static void RemoveDuplicateComponents<T>(Scene scene, T keepComponent) where T : Component
        {
            var components = Object.FindObjectsOfType<T>(true);
            for (var i = 0; i < components.Length; i++)
            {
                var candidate = components[i];
                if (candidate == null ||
                    candidate == keepComponent ||
                    candidate.gameObject.scene != scene)
                {
                    continue;
                }

                Undo.DestroyObjectImmediate(candidate);
            }
        }

        private static T LoadAsset<T>(params string[] paths) where T : Object
        {
            for (var i = 0; i < paths.Length; i++)
            {
                var loaded = AssetDatabase.LoadAssetAtPath<T>(paths[i]);
                if (loaded != null)
                {
                    return loaded;
                }
            }

            if (paths.Length > 0)
            {
                Debug.LogError($"Missing required asset: {paths[0]}");
            }

            return null;
        }

        private static void AddLine(ISet<Vector2Int> cells, Vector2Int start, Vector2Int end)
        {
            if (start.x != end.x && start.y != end.y)
            {
                return;
            }

            var dx = end.x == start.x ? 0 : (end.x > start.x ? 1 : -1);
            var dy = end.y == start.y ? 0 : (end.y > start.y ? 1 : -1);
            var current = start;
            cells.Add(current);

            while (current != end)
            {
                current += new Vector2Int(dx, dy);
                cells.Add(current);
            }
        }

        private static SerializedProperty RequireProperty(SerializedObject serializedObject, string propertyName, string typeName)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Missing serialized field '{propertyName}' on {typeName}.");
            }

            return property;
        }

        private static SerializedProperty RequireProperty(SerializedProperty serializedProperty, string propertyName, string typeName)
        {
            var property = serializedProperty.FindPropertyRelative(propertyName);
            if (property == null)
            {
                Debug.LogError($"Missing serialized field '{propertyName}' on {typeName}.");
            }

            return property;
        }
    }
}
