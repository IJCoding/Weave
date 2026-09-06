using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Weave.Data;
using Weave.Runtime;
using Weave.Simulation;

namespace Weave.Presentation
{
    [RequireComponent(typeof(PrototypeGameSession))]
    public sealed class PrototypeShowcaseController : MonoBehaviour
    {
        private const string RowanDecisionKey = "ROWAN_RESPONSE";
        private const string ShareSuppliesOptionId = "share_supplies";
        private const string StayAtWorkshopOptionId = "stay_at_workshop";
        private const string HelpRowanOptionId = "help_rowan";
        private const string FocusOnWorkOptionId = "focus_on_work";
        private const string RowanHelpedFlag = "rowan_helped";
        private const string SuppliesSharedFlag = "supplies_shared";

        private sealed class ShowcaseScenario
        {
            public GameCalendarDefinition Calendar;
            public CharacterDefinition ControlledCharacter;
            public List<LocationDefinition> Locations;
            public List<CharacterDefinition> Characters;
            public List<TaskDefinition> Tasks;
            public EventDefinition PlayerEvent;
            public EventDefinition NpcEvent;
        }

        private readonly Dictionary<string, SpriteRenderer> characterMarkers = new Dictionary<string, SpriteRenderer>();
        private readonly List<UnityEngine.Object> runtimeDefinitions = new List<UnityEngine.Object>();

        private PrototypeGameSession session;
        private PlayerCanonState playerCanon = new PlayerCanonState();
        private CharacterDefinition controlledCharacter;
        private List<LocationDefinition> locations = new List<LocationDefinition>();
        private List<CharacterDefinition> characters = new List<CharacterDefinition>();
        private List<TaskDefinition> tasks = new List<TaskDefinition>();
        private EventDefinition playerEvent;
        private EventDefinition npcEvent;
        private TaskDefinition activeTask;
        private Sprite markerSprite;
        private GameObject runtimeVisualRoot;
        private string statusMessage = "The prototype is ready to demonstrate travel, task resolution, events, canon, and day progression.";
        private List<string> seasonNames = new List<string>();

        private void Awake()
        {
            session = GetComponent<PrototypeGameSession>();

            if (session == null)
            {
                session = gameObject.AddComponent<PrototypeGameSession>();
            }

            var scenario = CreateScenario();

            controlledCharacter = scenario.ControlledCharacter;
            locations = scenario.Locations;
            characters = scenario.Characters;
            tasks = scenario.Tasks;
            playerEvent = scenario.PlayerEvent;
            npcEvent = scenario.NpcEvent;
            seasonNames = new List<string>(scenario.Calendar.Seasons);

            session.Configure(scenario.Calendar, locations, characters, tasks);
            session.StartRun(controlledCharacter);

            ConfigureCamera();
            BuildRuntimeVisuals();
            UpdateCharacterMarkers();
        }

        private void Update()
        {
            UpdateCharacterMarkers();
        }

        private void OnDestroy()
        {
            if (runtimeVisualRoot != null)
            {
                DestroyObject(runtimeVisualRoot);
            }

            if (markerSprite != null)
            {
                DestroyObject(markerSprite.texture);
                DestroyObject(markerSprite);
            }

            foreach (var runtimeDefinition in runtimeDefinitions)
            {
                DestroyObject(runtimeDefinition);
            }

            runtimeDefinitions.Clear();
        }

        private void OnGUI()
        {
            if (session == null || session.RunState == null || controlledCharacter == null)
            {
                return;
            }

            var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
            var availableTasks = session.GetPlayerTasks();
            var currentSeason = GetCurrentSeasonName();

            GUILayout.BeginArea(new Rect(16f, 16f, 360f, 520f), GUI.skin.box);
            GUILayout.Label("Weave Prototype Showcase");
            GUILayout.Label($"Day {session.RunState.Calendar.DayOfSeason} of {currentSeason}, Year {session.RunState.Calendar.Year}");
            GUILayout.Label($"Controlled villager: {controlledCharacter.DisplayName} ({controlledCharacter.Profession})");
            GUILayout.Label($"Current location: {GetLocationDisplayName(controlledState.CurrentLocationId)}");
            GUILayout.Label($"Current task: {GetTaskDisplayName(controlledState.CurrentTaskId)}");
            GUILayout.Label($"Travel progress: {(controlledState.IsTravelling ? $"{Mathf.RoundToInt(controlledState.TravelProgress * 100f)}%" : "Idle")}");
            GUILayout.Label($"Resources: {FormatResources(controlledState)}");
            GUILayout.Label($"World flags: {FormatWorldFlags()}\n");

            if (GUILayout.Button("Restart demo run"))
            {
                RestartRun();
            }

            GUILayout.Space(8f);
            GUILayout.Label("Tasks");

            if (!controlledState.IsTravelling && string.IsNullOrEmpty(controlledState.CurrentTaskId))
            {
                foreach (var task in availableTasks)
                {
                    if (GUILayout.Button($"Travel for: {task.DisplayName}"))
                    {
                        activeTask = task;
                        session.AssignPlayerTask(task);
                        statusMessage = $"{controlledCharacter.DisplayName} started travelling to {task.RequiredLocation.DisplayName}.";
                    }
                }
            }
            else if (controlledState.IsTravelling)
            {
                if (GUILayout.Button("Advance travel by 25%"))
                {
                    session.TickCharacterTravel(controlledCharacter.CharacterId, 0.25f);
                    var updatedState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
                    statusMessage = updatedState.IsTravelling
                        ? $"{controlledCharacter.DisplayName} advanced toward {GetLocationDisplayName(updatedState.TravelDestinationLocationId)}."
                        : $"{controlledCharacter.DisplayName} arrived at {GetLocationDisplayName(updatedState.CurrentLocationId)}.";
                }

                if (GUILayout.Button("Arrive now"))
                {
                    session.TickCharacterTravel(
                        controlledCharacter.CharacterId,
                        Mathf.Max(1f - controlledState.TravelProgress, 0f));
                    var updatedState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
                    statusMessage = updatedState.IsTravelling
                        ? $"{controlledCharacter.DisplayName} advanced toward {GetLocationDisplayName(updatedState.TravelDestinationLocationId)}."
                        : $"{controlledCharacter.DisplayName} arrived at {GetLocationDisplayName(updatedState.CurrentLocationId)}.";
                }
            }
            else if (activeTask != null && controlledState.CurrentTaskId == activeTask.TaskId)
            {
                if (GUILayout.Button($"Resolve task: {activeTask.DisplayName}"))
                {
                    session.ResolvePlayerTask(activeTask);
                    var updatedState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
                    statusMessage = $"Resolved {activeTask.DisplayName}. {controlledCharacter.DisplayName} now has {FormatResources(updatedState)}.";
                    activeTask = null;
                }
            }

            GUILayout.Space(8f);
            GUILayout.Label("Player event");
            GUILayout.Label("Village Request: Rowan asks whether you will back his workshop plan.");

            if (GUILayout.Button("Choose: Help Rowan"))
            {
                var resolution = session.ResolvePlayerEvent(playerEvent, HelpRowanOptionId);
                statusMessage = string.IsNullOrEmpty(resolution.SummaryText) ? "Resolved the player event." : resolution.SummaryText;
            }

            if (GUILayout.Button("Choose: Focus on work"))
            {
                var resolution = session.ResolvePlayerEvent(playerEvent, FocusOnWorkOptionId);
                statusMessage = string.IsNullOrEmpty(resolution.SummaryText) ? "Resolved the player event." : resolution.SummaryText;
            }

            GUILayout.Space(8f);
            GUILayout.Label("NPC canon event");

            if (GUILayout.Button("Use Rowan's developer canon"))
            {
                playerCanon = new PlayerCanonState();
                statusMessage = "Rowan will follow the authored developer canon again.";
            }

            if (GUILayout.Button("Override Rowan canon: stay at workshop"))
            {
                playerCanon.SetOption("rowan", RowanDecisionKey, StayAtWorkshopOptionId);
                statusMessage = "Rowan now follows the player-canon override instead of the developer default.";
            }

            if (GUILayout.Button("Resolve Rowan canon event"))
            {
                var resolution = session.ResolveNpcEvent(playerCanon, npcEvent);
                statusMessage = string.IsNullOrEmpty(resolution.SummaryText) ? "Resolved Rowan's event." : resolution.SummaryText;
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Advance day"))
            {
                activeTask = null;
                session.AdvanceDay();
                statusMessage = "Advanced to the next day and cleared the daily travel/task state.";
            }

            GUILayout.Space(8f);
            GUILayout.Label("Status");
            GUILayout.Label(statusMessage);
            GUILayout.EndArea();
        }

        private string GetCurrentSeasonName()
        {
            var seasonIndex = session.RunState.Calendar.SeasonIndex;

            if (seasonIndex < 0 || seasonIndex >= seasonNames.Count)
            {
                return "Unknown";
            }

            return seasonNames[seasonIndex];
        }

        private void RestartRun()
        {
            playerCanon = new PlayerCanonState();
            activeTask = null;
            session.StartRun(controlledCharacter);
            statusMessage = "Restarted the run from day one with the authored starting data.";
        }

        private void ConfigureCamera()
        {
            if (Camera.main == null)
            {
                return;
            }

            Camera.main.orthographic = true;
            Camera.main.orthographicSize = 5.75f;
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.backgroundColor = new Color(0.10f, 0.13f, 0.18f, 1f);
        }

        private void BuildRuntimeVisuals()
        {
            markerSprite = CreateMarkerSprite();
            runtimeVisualRoot = new GameObject("Prototype Showcase Runtime Visuals");
            characterMarkers.Clear();

            foreach (var location in locations)
            {
                var locationObject = new GameObject($"Location - {location.DisplayName}");
                locationObject.transform.SetParent(runtimeVisualRoot.transform, false);
                locationObject.transform.position = new Vector3(location.MapPosition.x, location.MapPosition.y, 0f);

                var renderer = locationObject.AddComponent<SpriteRenderer>();
                renderer.sprite = markerSprite;
                renderer.color = GetLocationColor(location.LocationType);
                renderer.sortingOrder = 0;
                locationObject.transform.localScale = new Vector3(0.7f, 0.7f, 1f);

                CreateTextLabel(locationObject.transform, location.DisplayName, new Vector3(0f, 0.7f, 0f), 0.28f, Color.white);
            }

            foreach (var character in characters)
            {
                var characterObject = new GameObject($"Character - {character.DisplayName}");
                characterObject.transform.SetParent(runtimeVisualRoot.transform, false);

                var renderer = characterObject.AddComponent<SpriteRenderer>();
                renderer.sprite = markerSprite;
                renderer.color = character.MapColor;
                renderer.sortingOrder = 2;
                characterObject.transform.localScale = new Vector3(0.35f, 0.35f, 1f);

                CreateTextLabel(characterObject.transform, character.DisplayName, new Vector3(0f, -0.55f, 0f), 0.18f, character.MapColor);
                characterMarkers[character.CharacterId] = renderer;
            }
        }

        private void UpdateCharacterMarkers()
        {
            if (session == null || session.RunState == null)
            {
                return;
            }

            foreach (var character in characters)
            {
                if (!characterMarkers.TryGetValue(character.CharacterId, out var marker))
                {
                    continue;
                }

                var position = session.GetCharacterMapPosition(character.CharacterId);
                marker.transform.position = new Vector3(position.x, position.y, -0.1f);
            }
        }

        private ShowcaseScenario CreateScenario()
        {
            runtimeDefinitions.Clear();
            var scenario = new ShowcaseScenario();
            scenario.Calendar = Track(CreateCalendar());

            var villageSquare = Track(CreateLocation("village_square", "Village Square", LocationType.Village, new Vector2(0f, 0f)));
            var easternMine = Track(CreateLocation("eastern_mine", "Eastern Mine", LocationType.Mine, new Vector2(3.6f, 1.8f)));
            var pineForest = Track(CreateLocation("pine_forest", "Pine Forest", LocationType.Forest, new Vector2(-3.5f, 1.5f)));
            var riversideFarm = Track(CreateLocation("riverside_farm", "Riverside Farm", LocationType.Farm, new Vector2(-2.75f, -2.25f)));
            var oldWorkshop = Track(CreateLocation("old_workshop", "Old Workshop", LocationType.Workshop, new Vector2(2.2f, -2f)));

            scenario.Locations = new List<LocationDefinition>
            {
                villageSquare,
                easternMine,
                pineForest,
                riversideFarm,
                oldWorkshop
            };

            var mina = Track(CreateCharacter(
                "mina",
                "Mina",
                ProfessionType.Miner,
                new Color(0.93f, 0.75f, 0.33f, 1f),
                villageSquare,
                new List<ResourceAmount>
                {
                    new ResourceAmount { ResourceId = "iron", Amount = 0 },
                    new ResourceAmount { ResourceId = "wood", Amount = 0 },
                    new ResourceAmount { ResourceId = "goodwill", Amount = 0 },
                    new ResourceAmount { ResourceId = "tools", Amount = 0 }
                },
                new List<CanonDecisionDefault>()));

            var rowan = Track(CreateCharacter(
                "rowan",
                "Rowan",
                ProfessionType.Blacksmith,
                new Color(0.78f, 0.40f, 0.28f, 1f),
                oldWorkshop,
                new List<ResourceAmount>
                {
                    new ResourceAmount { ResourceId = "tools", Amount = 2 }
                },
                new List<CanonDecisionDefault>
                {
                    new CanonDecisionDefault { DecisionKey = RowanDecisionKey, DefaultOptionId = ShareSuppliesOptionId }
                }));

            var elara = Track(CreateCharacter(
                "elara",
                "Elara",
                ProfessionType.Farmer,
                new Color(0.40f, 0.81f, 0.49f, 1f),
                riversideFarm,
                new List<ResourceAmount>
                {
                    new ResourceAmount { ResourceId = "grain", Amount = 4 }
                },
                new List<CanonDecisionDefault>()));

            scenario.ControlledCharacter = mina;
            scenario.Characters = new List<CharacterDefinition> { mina, rowan, elara };

            scenario.Tasks = new List<TaskDefinition>
            {
                Track(CreateTask(
                    "mine_iron",
                    "Mine Iron",
                    easternMine,
                    new List<CharacterDefinition> { mina },
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "iron", Amount = 2 } })),
                Track(CreateTask(
                    "gather_timber",
                    "Gather Timber",
                    pineForest,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "wood", Amount = 3 } })),
                Track(CreateTask(
                    "inspect_workshop",
                    "Inspect Workshop",
                    oldWorkshop,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "goodwill", Amount = 1 } }))
            };

            scenario.PlayerEvent = Track(CreatePlayerEvent(mina));
            scenario.NpcEvent = Track(CreateNpcEvent(mina, rowan));
            return scenario;
        }

        private static GameCalendarDefinition CreateCalendar()
        {
            var calendar = ScriptableObject.CreateInstance<GameCalendarDefinition>();
            SerializedFieldUtility.SetPrivateField(calendar, "startingYear", 1);
            SerializedFieldUtility.SetPrivateField(calendar, "seasons", new List<string> { "Spring", "Summer", "Autumn", "Winter" });
            SerializedFieldUtility.SetPrivateField(calendar, "daysPerSeason", 5);
            return calendar;
        }

        private static LocationDefinition CreateLocation(string id, string displayName, LocationType locationType, Vector2 mapPosition)
        {
            var location = ScriptableObject.CreateInstance<LocationDefinition>();
            SerializedFieldUtility.SetPrivateField(location, "locationId", id);
            SerializedFieldUtility.SetPrivateField(location, "displayName", displayName);
            SerializedFieldUtility.SetPrivateField(location, "locationType", locationType);
            SerializedFieldUtility.SetPrivateField(location, "mapPosition", mapPosition);
            return location;
        }

        private static CharacterDefinition CreateCharacter(
            string id,
            string displayName,
            ProfessionType profession,
            Color mapColor,
            LocationDefinition homeLocation,
            List<ResourceAmount> startingResources,
            List<CanonDecisionDefault> developerCanon)
        {
            var character = ScriptableObject.CreateInstance<CharacterDefinition>();
            SerializedFieldUtility.SetPrivateField(character, "characterId", id);
            SerializedFieldUtility.SetPrivateField(character, "displayName", displayName);
            SerializedFieldUtility.SetPrivateField(character, "profession", profession);
            SerializedFieldUtility.SetPrivateField(character, "mapColor", mapColor);
            SerializedFieldUtility.SetPrivateField(character, "homeLocation", homeLocation);
            SerializedFieldUtility.SetPrivateField(character, "startingResources", startingResources);
            SerializedFieldUtility.SetPrivateField(character, "developerCanon", developerCanon);
            return character;
        }

        private static TaskDefinition CreateTask(
            string taskId,
            string displayName,
            LocationDefinition requiredLocation,
            List<CharacterDefinition> eligibleCharacters,
            List<ResourceAmount> actorResourceChanges)
        {
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SerializedFieldUtility.SetPrivateField(task, "taskId", taskId);
            SerializedFieldUtility.SetPrivateField(task, "displayName", displayName);
            SerializedFieldUtility.SetPrivateField(task, "requiredLocation", requiredLocation);
            SerializedFieldUtility.SetPrivateField(task, "eligibleCharacters", eligibleCharacters);
            SerializedFieldUtility.SetPrivateField(task, "requiredWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "blockedWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", actorResourceChanges);
            SerializedFieldUtility.SetPrivateField(task, "followUpEvent", null);
            return task;
        }

        private static EventDefinition CreatePlayerEvent(CharacterDefinition mina)
        {
            var eventDefinition = ScriptableObject.CreateInstance<EventDefinition>();
            SerializedFieldUtility.SetPrivateField(eventDefinition, "eventId", "village_request");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "prompt", "Rowan asks Mina to back his workshop expansion plan.");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "decisionMaker", mina);
            SerializedFieldUtility.SetPrivateField(eventDefinition, "decisionKey", "MINA_REQUEST");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "triggerConditions", new List<WorldFlagRequirement>());
            SerializedFieldUtility.SetPrivateField(
                eventDefinition,
                "options",
                new List<DecisionOptionDefinition>
                {
                    CreateOption(
                        HelpRowanOptionId,
                        "Help Rowan",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "Mina backs Rowan's idea, earning goodwill and setting up the workshop for help.",
                                new List<WorldFlagRequirement>(),
                                new List<WorldFlagMutation>
                                {
                                    new WorldFlagMutation { FlagId = RowanHelpedFlag, SetPresent = true }
                                },
                                new List<CharacterResourceDelta>
                                {
                                    new CharacterResourceDelta { Character = mina, ResourceId = "goodwill", Amount = 1 }
                                })
                        }),
                    CreateOption(
                        FocusOnWorkOptionId,
                        "Focus on Work",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "Mina keeps her attention on daily work, so Rowan gets no extra support.",
                                new List<WorldFlagRequirement>(),
                                new List<WorldFlagMutation>
                                {
                                    new WorldFlagMutation { FlagId = RowanHelpedFlag, SetPresent = false },
                                    new WorldFlagMutation { FlagId = SuppliesSharedFlag, SetPresent = false }
                                },
                                new List<CharacterResourceDelta>())
                        })
                });
            return eventDefinition;
        }

        private static EventDefinition CreateNpcEvent(CharacterDefinition mina, CharacterDefinition rowan)
        {
            var eventDefinition = ScriptableObject.CreateInstance<EventDefinition>();
            SerializedFieldUtility.SetPrivateField(eventDefinition, "eventId", "rowan_response");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "prompt", "Rowan decides whether to share workshop supplies with the village.");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "decisionMaker", rowan);
            SerializedFieldUtility.SetPrivateField(eventDefinition, "decisionKey", RowanDecisionKey);
            SerializedFieldUtility.SetPrivateField(eventDefinition, "triggerConditions", new List<WorldFlagRequirement>());
            SerializedFieldUtility.SetPrivateField(
                eventDefinition,
                "options",
                new List<DecisionOptionDefinition>
                {
                    CreateOption(
                        ShareSuppliesOptionId,
                        "Share supplies",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "Because Mina helped him earlier, Rowan follows his developer-canon instinct and shares workshop supplies.",
                                new List<WorldFlagRequirement>
                                {
                                    new WorldFlagRequirement { FlagId = RowanHelpedFlag, MustBePresent = true }
                                },
                                new List<WorldFlagMutation>
                                {
                                    new WorldFlagMutation { FlagId = SuppliesSharedFlag, SetPresent = true }
                                },
                                new List<CharacterResourceDelta>
                                {
                                    new CharacterResourceDelta { Character = mina, ResourceId = "tools", Amount = 1 }
                                }),
                            CreateOutcome(
                                "Rowan wants to share supplies, but without prior support the outcome variant falls back to a cautious result.",
                                new List<WorldFlagRequirement>(),
                                new List<WorldFlagMutation>
                                {
                                    new WorldFlagMutation { FlagId = SuppliesSharedFlag, SetPresent = false }
                                },
                                new List<CharacterResourceDelta>())
                        }),
                    CreateOption(
                        StayAtWorkshopOptionId,
                        "Stay at workshop",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "The player-canon override keeps Rowan focused on his workshop, so nothing changes in the village today.",
                                new List<WorldFlagRequirement>(),
                                new List<WorldFlagMutation>
                                {
                                    new WorldFlagMutation { FlagId = SuppliesSharedFlag, SetPresent = false }
                                },
                                new List<CharacterResourceDelta>())
                        })
                });
            return eventDefinition;
        }

        private static DecisionOptionDefinition CreateOption(
            string optionId,
            string label,
            List<OutcomeVariantDefinition> outcomes)
        {
            var option = new DecisionOptionDefinition();
            SerializedFieldUtility.SetPrivateField(option, "optionId", optionId);
            SerializedFieldUtility.SetPrivateField(option, "label", label);
            SerializedFieldUtility.SetPrivateField(option, "outcomes", outcomes);
            return option;
        }

        private static OutcomeVariantDefinition CreateOutcome(
            string summaryText,
            List<WorldFlagRequirement> conditions,
            List<WorldFlagMutation> worldFlagMutations,
            List<CharacterResourceDelta> resourceChanges)
        {
            var outcome = new OutcomeVariantDefinition();
            SerializedFieldUtility.SetPrivateField(outcome, "summaryText", summaryText);
            SerializedFieldUtility.SetPrivateField(outcome, "conditions", conditions);
            SerializedFieldUtility.SetPrivateField(outcome, "worldFlagMutations", worldFlagMutations);
            SerializedFieldUtility.SetPrivateField(outcome, "resourceChanges", resourceChanges);
            return outcome;
        }

        private string GetLocationDisplayName(string locationId)
        {
            foreach (var location in locations)
            {
                if (location.LocationId == locationId)
                {
                    return location.DisplayName;
                }
            }

            return string.IsNullOrEmpty(locationId) ? "None" : locationId;
        }

        private string GetTaskDisplayName(string taskId)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                return "None";
            }

            foreach (var task in tasks)
            {
                if (task.TaskId == taskId)
                {
                    return task.DisplayName;
                }
            }

            return taskId;
        }

        private string FormatWorldFlags()
        {
            if (session.RunState.WorldFlags.Count == 0)
            {
                return "None";
            }

            var builder = new StringBuilder();
            var first = true;

            foreach (var flag in session.RunState.WorldFlags)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(flag);
                first = false;
            }

            return builder.ToString();
        }

        private static string FormatResources(CharacterState characterState)
        {
            var builder = new StringBuilder();
            var first = true;

            foreach (var resource in characterState.Resources)
            {
                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(resource.Key);
                builder.Append(':');
                builder.Append(' ');
                builder.Append(resource.Value);
                first = false;
            }

            return first ? "None" : builder.ToString();
        }

        private static Sprite CreateMarkerSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        private static void CreateTextLabel(Transform parent, string text, Vector3 localPosition, float characterSize, Color color)
        {
            var labelObject = new GameObject($"Label - {text}");
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;

            var textMesh = labelObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.characterSize = characterSize;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;
            textMesh.fontSize = 32;
        }

        private static Color GetLocationColor(LocationType locationType)
        {
            switch (locationType)
            {
                case LocationType.Mine:
                    return new Color(0.43f, 0.50f, 0.61f, 1f);
                case LocationType.Forest:
                    return new Color(0.23f, 0.53f, 0.28f, 1f);
                case LocationType.Farm:
                    return new Color(0.67f, 0.57f, 0.27f, 1f);
                case LocationType.Workshop:
                    return new Color(0.60f, 0.33f, 0.24f, 1f);
                default:
                    return new Color(0.24f, 0.37f, 0.50f, 1f);
            }
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }

        private T Track<T>(T target)
            where T : UnityEngine.Object
        {
            runtimeDefinitions.Add(target);
            return target;
        }
    }
}
