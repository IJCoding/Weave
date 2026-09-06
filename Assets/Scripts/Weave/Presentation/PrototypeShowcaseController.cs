using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Weave.Data;
using Weave.Runtime;
using Weave.Simulation;
using Weave.World;

namespace Weave.Presentation
{
    [RequireComponent(typeof(PrototypeGameSession))]
    public sealed class PrototypeShowcaseController : MonoBehaviour
    {
        private sealed class PopupChoice
        {
            public string Label;
            public Action OnSelected;
        }

        private sealed class TaskButtonView
        {
            public TaskDefinition Task;
            public Button Button;
            public RectTransform Root;
            public RectTransform PhaseContainer;
            public Image OverallFill;
            public Image ForegroundFill;
            public Text Label;
        }

        private readonly Dictionary<string, Transform> characterVisuals = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Image> workRings = new Dictionary<string, Image>();
        private readonly List<TaskButtonView> taskButtons = new List<TaskButtonView>();
        private readonly List<SimulationLogEntry> activityLogEntries = new List<SimulationLogEntry>();

        private PrototypeGameSession session;
        private AuthoredVillageScenario scenario;
        private AuthoredVillageWorldRegistry worldRegistry;
        private CharacterDefinition controlledCharacter;
        private Font uiFont;
        private PlayerCanonState playerCanon = new PlayerCanonState();
        private bool popupOwnsPause;
        private bool suppressRefresh;
        private Transform controlledCharacterVisual;

        private Canvas runtimeCanvas;
        private Text dayText;
        private Text timeRemainingText;
        private Text currentLocationText;
        private Text characterText;
        private Text storedResourcesText;
        private Text carriedResourcesText;
        private Text carryWeightText;
        private Text worldFlagsText;
        private Text actionHeaderText;
        private Text actionStatusText;
        private RectTransform taskButtonContainer;
        private Image pauseButtonImage;
        private Image playButtonImage;
        private Image fastForwardButtonImage;
        private Text activityConsoleText;
        private GameObject popupOverlay;
        private Text popupTitleText;
        private Text popupSourceText;
        private Text popupBodyText;
        private RectTransform popupChoiceContainer;

        private void Awake()
        {
            session = GetComponent<PrototypeGameSession>();
            scenario = GetComponent<AuthoredVillageScenario>();
            if (scenario == null)
            {
                Debug.LogError("PrototypeShowcaseController requires an AuthoredVillageScenario on the same GameObject.", this);
                enabled = false;
                return;
            }

            worldRegistry = scenario.WorldRegistry != null ? scenario.WorldRegistry : GetComponentInChildren<AuthoredVillageWorldRegistry>();
            if (worldRegistry == null)
            {
                Debug.LogError("PrototypeShowcaseController requires an AuthoredVillageWorldRegistry in the scene.", this);
                enabled = false;
                return;
            }

            worldRegistry.RefreshWorld();
            worldRegistry.ValidateUniqueLocationIds();
            worldRegistry.ValidateUniqueNpcIds();
            controlledCharacter = scenario.ControlledCharacter;
            if (controlledCharacter == null)
            {
                Debug.LogError("AuthoredVillageScenario requires a controlled character definition.", this);
                enabled = false;
                return;
            }

            if (worldRegistry.FindNpc(controlledCharacter.CharacterId) != null)
            {
                Debug.LogError($"Controlled character id '{controlledCharacter.CharacterId}' is also assigned to an authored NPC.", this);
                enabled = false;
                return;
            }

            session.SetAuthoredWorld(worldRegistry);
            session.Configure(
                scenario.CalendarDefinition,
                worldRegistry.BuildRuntimeLocations(),
                worldRegistry.BuildCharacters(controlledCharacter),
                worldRegistry.BuildStaticTasks(),
                scenario.Resources);
            ConfigureCamera();
            BuildUi();
            BuildCharacterVisuals();
            session.StateChanged += RefreshPresentation;
            session.SimulationAdvanced += HandleSimulationAdvanced;
            session.SimulationLogEntryAdded += HandleSimulationLogEntryAdded;
            AuthoredVillageLocation.Clicked += HandleLocationClicked;
            session.StartRun(controlledCharacter);
            RefreshPresentation();
            UpdateCharacterVisuals();
            RebuildActivityConsoleFromSession();
        }

        private void OnDestroy()
        {
            AuthoredVillageLocation.Clicked -= HandleLocationClicked;
            if (session != null)
            {
                session.StateChanged -= RefreshPresentation;
                session.SimulationAdvanced -= HandleSimulationAdvanced;
                session.SimulationLogEntryAdded -= HandleSimulationLogEntryAdded;
            }

            if (controlledCharacterVisual != null)
            {
                Destroy(controlledCharacterVisual.gameObject);
            }
        }

        private void Update()
        {
            UpdateCharacterVisuals();
        }

        private void ConfigureCamera()
        {
            if (Camera.main == null)
            {
                return;
            }

            Camera.main.orthographic = true;
            Camera.main.orthographicSize = 6.2f;
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.backgroundColor = new Color(0.10f, 0.12f, 0.16f, 1f);
        }

        private void BuildUi()
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            EnsureEventSystem();

            var canvasObject = new GameObject("Prototype HUD");
            runtimeCanvas = canvasObject.AddComponent<Canvas>();
            runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObject.AddComponent<GraphicRaycaster>();

            var root = CreateRect("HUD Root", runtimeCanvas.transform);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            var topBar = CreatePanel("Top Bar", root, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -84f), new Vector2(-12f, -12f), new Color(0.09f, 0.12f, 0.17f, 0.92f));
            dayText = CreateText("Day", topBar, new Vector2(0f, 0f), new Vector2(0.26f, 1f), new Vector2(18f, 0f), new Vector2(-8f, 0f), string.Empty, 22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            timeRemainingText = CreateText("Time", topBar, new Vector2(0.26f, 0f), new Vector2(0.48f, 1f), new Vector2(8f, 0f), new Vector2(-8f, 0f), string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            currentLocationText = CreateText("Location", topBar, new Vector2(0.48f, 0f), new Vector2(0.75f, 1f), new Vector2(8f, 0f), new Vector2(-8f, 0f), string.Empty, 18, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            characterText = CreateText("Character", topBar, new Vector2(0.75f, 0f), new Vector2(0.86f, 1f), new Vector2(8f, 0f), new Vector2(-8f, 0f), string.Empty, 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);

            var speedBar = CreateRect("Speed Buttons", topBar);
            speedBar.anchorMin = new Vector2(0.86f, 0.16f);
            speedBar.anchorMax = new Vector2(0.99f, 0.84f);
            speedBar.offsetMin = Vector2.zero;
            speedBar.offsetMax = Vector2.zero;
            var speedLayout = speedBar.gameObject.AddComponent<HorizontalLayoutGroup>();
            speedLayout.spacing = 8f;
            speedLayout.childControlHeight = true;
            speedLayout.childControlWidth = true;
            speedLayout.childForceExpandHeight = true;
            speedLayout.childForceExpandWidth = true;
            pauseButtonImage = CreateButton(speedBar, "Pause", "II", () => session.SetSimulationSpeed(SimulationSpeedMode.Paused)).Background;
            playButtonImage = CreateButton(speedBar, "Play", ">", () => session.SetSimulationSpeed(SimulationSpeedMode.Normal)).Background;
            fastForwardButtonImage = CreateButton(speedBar, "Fast", ">>", () => session.SetSimulationSpeed(SimulationSpeedMode.FastForward)).Background;

            var leftPanel = CreatePanel("Actions", root, new Vector2(0f, 0f), new Vector2(0.25f, 1f), new Vector2(12f, 12f), new Vector2(-6f, -96f), new Color(0.08f, 0.10f, 0.15f, 0.9f));
            CreateText("Actions Title", leftPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -48f), new Vector2(-16f, -12f), "CURRENT LOCATION", 22, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            actionHeaderText = CreateText("Actions Header", leftPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -82f), new Vector2(-16f, -50f), string.Empty, 20, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.90f, 0.96f, 1f, 1f));
            actionStatusText = CreateText("Actions Status", leftPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -128f), new Vector2(-16f, -88f), string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.74f, 0.81f, 0.89f, 1f));
            taskButtonContainer = CreateRect("Task Buttons", leftPanel);
            taskButtonContainer.anchorMin = new Vector2(0f, 0f);
            taskButtonContainer.anchorMax = new Vector2(1f, 1f);
            taskButtonContainer.offsetMin = new Vector2(16f, 16f);
            taskButtonContainer.offsetMax = new Vector2(-16f, -138f);
            var taskLayout = taskButtonContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            taskLayout.spacing = 10f;
            taskLayout.childControlHeight = true;
            taskLayout.childControlWidth = true;
            taskLayout.childForceExpandHeight = false;
            taskLayout.childForceExpandWidth = true;
            taskButtonContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var rightPanel = CreatePanel("Status", root, new Vector2(0.75f, 0f), new Vector2(1f, 1f), new Vector2(6f, 12f), new Vector2(-12f, -96f), new Color(0.08f, 0.10f, 0.15f, 0.9f));
            storedResourcesText = CreateText("Stored", rightPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -52f), new Vector2(-16f, -12f), string.Empty, 16, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            carriedResourcesText = CreateText("Carried", rightPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -112f), new Vector2(-16f, -56f), string.Empty, 15, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            carryWeightText = CreateText("Carry Weight", rightPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -144f), new Vector2(-16f, -118f), string.Empty, 15, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            worldFlagsText = CreateText("Flags", rightPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -202f), new Vector2(-16f, -150f), string.Empty, 14, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            CreateText("Activity Title", rightPanel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(16f, -246f), new Vector2(-16f, -218f), "ACTIVITY LOG", 18, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            activityConsoleText = CreateText("Activity", rightPanel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 16f), new Vector2(-16f, -252f), string.Empty, 14, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.84f, 0.90f, 0.97f, 1f));
            activityConsoleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            activityConsoleText.verticalOverflow = VerticalWrapMode.Overflow;

            BuildPopup(root);
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        private void BuildPopup(RectTransform root)
        {
            popupOverlay = CreatePanel("Popup Overlay", root, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.6f)).gameObject;
            popupOverlay.SetActive(false);
            var panel = CreatePanel("Popup Panel", popupOverlay.transform, new Vector2(0.22f, 0.2f), new Vector2(0.78f, 0.8f), Vector2.zero, Vector2.zero, new Color(0.10f, 0.12f, 0.16f, 0.98f));
            popupTitleText = CreateText("Popup Title", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -56f), new Vector2(-24f, -16f), string.Empty, 26, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white);
            popupSourceText = CreateText("Popup Source", panel, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -92f), new Vector2(-24f, -60f), string.Empty, 18, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.44f, 0.77f, 0.98f, 1f));
            popupBodyText = CreateText("Popup Body", panel, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(24f, 104f), new Vector2(-24f, -108f), string.Empty, 22, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.95f, 0.97f, 1f, 1f));
            popupBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            popupBodyText.verticalOverflow = VerticalWrapMode.Overflow;
            popupChoiceContainer = CreateRect("Popup Choices", panel);
            popupChoiceContainer.anchorMin = new Vector2(0f, 0f);
            popupChoiceContainer.anchorMax = new Vector2(1f, 0f);
            popupChoiceContainer.offsetMin = new Vector2(24f, 24f);
            popupChoiceContainer.offsetMax = new Vector2(-24f, 88f);
            var layout = popupChoiceContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            popupChoiceContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildCharacterVisuals()
        {
            characterVisuals.Clear();
            workRings.Clear();

            foreach (var npc in worldRegistry.Npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.CharacterId))
                {
                    continue;
                }

                characterVisuals[npc.CharacterId] = npc.transform;
                workRings[npc.CharacterId] = CreateWorkRing(npc.transform, $"{npc.CharacterId} Ring");
            }

            var playerVisual = new GameObject("Controlled Character");
            playerVisual.transform.position = Vector3.zero;
            var renderer = playerVisual.AddComponent<SpriteRenderer>();
            renderer.sprite = PrototypeSpriteLibrary.GetCircleSprite();
            renderer.color = controlledCharacter.MapColor;
            renderer.transform.localScale = new Vector3(0.48f, 0.48f, 1f);
            renderer.sortingOrder = 11;
            var label = new GameObject("Label").AddComponent<TextMesh>();
            label.transform.SetParent(playerVisual.transform, false);
            label.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            label.text = controlledCharacter.DisplayName;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.12f;
            label.fontSize = 48;
            label.color = Color.white;
            controlledCharacterVisual = playerVisual.transform;
            characterVisuals[controlledCharacter.CharacterId] = controlledCharacterVisual;
            workRings[controlledCharacter.CharacterId] = CreateWorkRing(controlledCharacterVisual, "Controlled Ring");
        }

        private void RefreshPresentation()
        {
            if (session == null || session.RunState == null || controlledCharacter == null || suppressRefresh)
            {
                return;
            }

            var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
            var actionProgress = session.GetActionProgressForCharacter(controlledCharacter.CharacterId);
            characterText.text = controlledCharacter.DisplayName;
            currentLocationText.text = $"Location: {FormatLocationState(controlledState)}";
            dayText.text = $"{GetCurrentSeasonName()} — Day {session.RunState.Calendar.DayOfSeason}";
            timeRemainingText.text = $"Time Left: {FormatDuration(session.RunState.DayTimer.RemainingSeconds)}";
            storedResourcesText.text = $"Stored\n{FormatResourceLines(controlledState.StoredResources)}";
            carriedResourcesText.text = $"Carried\n{FormatResourceLines(controlledState.CarriedResources)}";
            carryWeightText.text = $"Carry Weight: {session.GetCharacterCarriedWeight(controlledCharacter.CharacterId):0.##}";
            worldFlagsText.text = $"World Flags\n{FormatWorldFlags()}";
            actionHeaderText.text = GetLocationDisplayName(controlledState.CurrentLocationId).ToUpperInvariant();
            actionStatusText.text = controlledState.IsTravelling
                ? $"Travelling to {GetLocationDisplayName(controlledState.TravelDestinationLocationId)}"
                : controlledState.IsWorkingOnTask
                    ? "Action in progress"
                    : "Available Actions";
            RefreshTaskButtons(controlledState, actionProgress);
            RefreshLocationVisuals(controlledState);
            RefreshSpeedButtons();
        }

        private void RefreshLocationVisuals(CharacterState controlledState)
        {
            foreach (var location in worldRegistry.Locations)
            {
                if (location == null)
                {
                    continue;
                }

                var state = LocationVisualState.Normal;
                if (location.LocationId == controlledState.CurrentLocationId)
                {
                    state = LocationVisualState.Current;
                }
                else if (controlledState.IsTravelling && location.LocationId == controlledState.TravelDestinationLocationId)
                {
                    state = LocationVisualState.Destination;
                }

                location.SetVisualState(state);
            }
        }

        private void RefreshSpeedButtons()
        {
            RefreshSpeedButton(pauseButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.Paused, session.EffectiveSpeedMode == SimulationSpeedMode.Paused);
            RefreshSpeedButton(playButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.Normal, session.EffectiveSpeedMode == SimulationSpeedMode.Normal);
            RefreshSpeedButton(fastForwardButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.FastForward, session.EffectiveSpeedMode == SimulationSpeedMode.FastForward);
        }

        private void RefreshSpeedButton(Image buttonImage, bool selected, bool active)
        {
            buttonImage.color = active
                ? new Color(0.33f, 0.76f, 0.46f, 1f)
                : selected
                    ? new Color(0.28f, 0.38f, 0.52f, 1f)
                    : new Color(0.18f, 0.22f, 0.27f, 1f);
        }

        private void RefreshTaskButtons(CharacterState controlledState, ActionProgressSummary actionProgress)
        {
            var actions = controlledState.IsTravelling || controlledState.IsWorkingOnTask
                ? new List<TaskDefinition>()
                : session.GetCurrentLocationActions();
            var hasActions = actions.Count > 0;
            if (!controlledState.IsTravelling && !controlledState.IsWorkingOnTask && !hasActions)
            {
                actionStatusText.text = "No actions available at this location.";
            }

            if (controlledState.HasActiveTask)
            {
                var activeTask = FindActionTask(controlledState.CurrentTaskId, actions);
                if (activeTask == null)
                {
                    activeTask = CreatePlaceholderTask(controlledState.CurrentTaskId);
                }

                EnsureTaskButtonCount(1);
                UpdateTaskButton(taskButtons[0], activeTask, actionProgress, true, true);
                HideUnusedTaskButtons(1);
                return;
            }

            EnsureTaskButtonCount(actions.Count);
            for (var index = 0; index < actions.Count; index++)
            {
                UpdateTaskButton(taskButtons[index], actions[index], session.GetTaskPlanPreview(controlledCharacter.CharacterId, actions[index]), true, false);
            }

            HideUnusedTaskButtons(actions.Count);
        }

        private TaskDefinition CreatePlaceholderTask(string taskId)
        {
            var task = ScriptableObject.CreateInstance<TaskDefinition>();
            SerializedFieldUtility.SetPrivateField(task, "taskId", taskId);
            SerializedFieldUtility.SetPrivateField(task, "displayName", taskId);
            SerializedFieldUtility.SetPrivateField(task, "requiredWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "blockedWorldFlags", new List<string>());
            SerializedFieldUtility.SetPrivateField(task, "eligibleCharacters", new List<CharacterDefinition>());
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", new List<ResourceAmount>());
            SerializedFieldUtility.SetPrivateField(task, "durationSeconds", 0f);
            SerializedFieldUtility.SetPrivateField(task, "requiredLocationId", string.Empty);
            return task;
        }

        private static TaskDefinition FindActionTask(string taskId, List<TaskDefinition> actions)
        {
            foreach (var action in actions)
            {
                if (action != null && action.TaskId == taskId)
                {
                    return action;
                }
            }

            return null;
        }

        private void EnsureTaskButtonCount(int requiredCount)
        {
            while (taskButtons.Count < requiredCount)
            {
                var buttonView = CreateButton(taskButtonContainer, "Task Button", string.Empty, null);
                var phaseContainer = CreatePanel("Task Phase", buttonView.Button.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10f, 6f), new Vector2(-10f, 16f), new Color(0.09f, 0.11f, 0.14f, 1f));
                phaseContainer.GetComponent<Image>().raycastTarget = false;
                var overallFill = CreatePanel("Task Overall", phaseContainer, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(0.24f, 0.30f, 0.36f, 0.65f)).GetComponent<Image>();
                overallFill.raycastTarget = false;
                var foregroundFill = CreatePanel("Task Current", phaseContainer, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero, new Color(0.24f, 0.82f, 0.36f, 1f)).GetComponent<Image>();
                foregroundFill.raycastTarget = false;
                taskButtons.Add(new TaskButtonView
                {
                    Button = buttonView.Button,
                    Root = buttonView.Button.GetComponent<RectTransform>(),
                    PhaseContainer = phaseContainer,
                    OverallFill = overallFill,
                    ForegroundFill = foregroundFill,
                    Label = buttonView.Label
                });
            }
        }

        private void UpdateTaskButton(TaskButtonView taskButton, TaskDefinition task, ActionProgressSummary progress, bool interactable, bool showProgress)
        {
            taskButton.Task = task;
            taskButton.Root.gameObject.SetActive(true);
            taskButton.Button.onClick.RemoveAllListeners();
            taskButton.Button.onClick.AddListener(() => AssignTask(task));
            taskButton.Button.interactable = !showProgress &&
                interactable &&
                !session.RunState.GetCharacter(controlledCharacter.CharacterId).IsTravelling &&
                !session.RunState.GetCharacter(controlledCharacter.CharacterId).IsWorkingOnTask;
            taskButton.Label.alignment = TextAnchor.UpperLeft;
            taskButton.Label.text = $"{task.DisplayName} ({Mathf.RoundToInt(task.DurationSeconds)}s)\n{GetStatusLine(progress, task, showProgress)}";
            taskButton.ForegroundFill.color = GetPhaseColor(progress.CurrentPhase.PhaseType);
            SetAnchoredHorizontal(taskButton.OverallFill.rectTransform, 0f, showProgress ? progress.OverallProgress : 0f);
            SetAnchoredHorizontal(taskButton.ForegroundFill.rectTransform, 0f, showProgress ? progress.CurrentPhaseProgress : 0f);
            taskButton.OverallFill.gameObject.SetActive(progress.HasPhases && progress.OverallProgress > 0f);
            taskButton.ForegroundFill.gameObject.SetActive(showProgress && progress.HasPhases && progress.CurrentPhaseProgress > 0f);
        }

        private void HideUnusedTaskButtons(int visibleCount)
        {
            for (var index = visibleCount; index < taskButtons.Count; index++)
            {
                taskButtons[index].Root.gameObject.SetActive(false);
            }
        }

        private string GetStatusLine(ActionProgressSummary progress, TaskDefinition task, bool showProgress)
        {
            if (!showProgress)
            {
                return task.DisplayName.StartsWith("Talk to ", StringComparison.OrdinalIgnoreCase)
                    ? "NPC interaction"
                    : "Local action";
            }

            if (!progress.HasPhases)
            {
                return "Ready";
            }

            switch (progress.CurrentPhase.PhaseType)
            {
                case ActionPhaseType.Work:
                    return "Working";
                case ActionPhaseType.ReturnTravel:
                    return "Returning";
                default:
                    return "Preparing";
            }
        }

        private void HandleSimulationAdvanced(SimulationAdvanceResult result)
        {
            if (result.TaskCompleted && result.CompletedTaskFollowUpEvent != null)
            {
                ShowDecisionPopup(result.CompletedTaskFollowUpEvent);
            }
        }

        private void HandleSimulationLogEntryAdded(SimulationLogEntry _)
        {
            AppendLatestLogEntry();
        }

        private void RebuildActivityConsoleFromSession()
        {
            activityLogEntries.Clear();
            foreach (var entry in session.SimulationLogEntries)
            {
                activityLogEntries.Add(entry);
            }

            RewriteActivityConsoleText();
        }

        private void AppendLatestLogEntry()
        {
            if (session == null)
            {
                return;
            }

            if (session.SimulationLogEntries.Count == 0)
            {
                activityLogEntries.Clear();
                activityConsoleText.text = string.Empty;
                return;
            }

            if (session.SimulationLogEntries.Count != activityLogEntries.Count + 1)
            {
                RebuildActivityConsoleFromSession();
                return;
            }

            var entry = session.SimulationLogEntries[session.SimulationLogEntries.Count - 1];
            activityLogEntries.Add(entry);
            if (activityLogEntries.Count > 1)
            {
                activityConsoleText.text += "\n";
            }

            activityConsoleText.text += $"D{entry.DayOfSeason:00} {FormatDuration(entry.DayElapsedSeconds)} — [{entry.Category}] {entry.Message}";
        }

        private void RewriteActivityConsoleText()
        {
            var builder = new StringBuilder();
            for (var index = 0; index < activityLogEntries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append('\n');
                }

                builder.Append($"D{activityLogEntries[index].DayOfSeason:00} {FormatDuration(activityLogEntries[index].DayElapsedSeconds)} — [{activityLogEntries[index].Category}] {activityLogEntries[index].Message}");
            }

            activityConsoleText.text = builder.ToString();
        }

        private void UpdateCharacterVisuals()
        {
            if (session == null || session.RunState == null)
            {
                return;
            }

            foreach (var character in session.Characters)
            {
                if (character == null || !characterVisuals.TryGetValue(character.CharacterId, out var visualRoot))
                {
                    continue;
                }

                var position = session.GetCharacterWorldPosition(character.CharacterId);
                visualRoot.position = new Vector3(position.x, position.y, visualRoot.position.z);
                UpdateWorkRing(character.CharacterId);
            }
        }

        private void UpdateWorkRing(string characterId)
        {
            if (!workRings.TryGetValue(characterId, out var ring) || session.RunState == null)
            {
                return;
            }

            var state = session.RunState.GetCharacter(characterId);
            if (!state.IsWorkingOnTask || state.TaskDurationSeconds <= 0f)
            {
                ring.gameObject.SetActive(false);
                return;
            }

            var actionProgress = session.GetActionProgressForCharacter(characterId);
            if (!actionProgress.HasPhases || actionProgress.CurrentPhase.PhaseType != ActionPhaseType.Work)
            {
                ring.gameObject.SetActive(false);
                return;
            }

            ring.fillAmount = actionProgress.CurrentPhaseProgress;
            ring.color = new Color(0.87f, 0.23f, 0.23f, 1f);
            ring.gameObject.SetActive(true);
        }

        private void HandleLocationClicked(AuthoredVillageLocation location)
        {
            if (location == null || session.RunState == null)
            {
                return;
            }

            var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
            if (controlledState.IsTravelling || controlledState.IsWorkingOnTask)
            {
                return;
            }

            if (controlledState.CurrentLocationId != location.LocationId)
            {
                session.RequestPlayerTravel(location.LocationId);
            }

            RefreshPresentation();
        }

        private void AssignTask(TaskDefinition task)
        {
            if (task == null || session.RunState == null)
            {
                return;
            }

            if (task.TaskId.StartsWith("talk::", StringComparison.Ordinal) && task.FollowUpEvent != null)
            {
                ShowDecisionPopup(task.FollowUpEvent);
                return;
            }

            session.AssignPlayerTask(task);
            RefreshPresentation();
        }

        private void ShowDecisionPopup(EventDefinition eventDefinition)
        {
            if (eventDefinition == null || popupOverlay.activeSelf)
            {
                return;
            }

            popupOverlay.SetActive(true);
            popupTitleText.text = string.IsNullOrEmpty(eventDefinition.Title) ? eventDefinition.EventId : eventDefinition.Title;
            popupSourceText.text = eventDefinition.SourceLabel;
            popupSourceText.gameObject.SetActive(!string.IsNullOrEmpty(popupSourceText.text));
            popupBodyText.text = eventDefinition.Prompt;
            session.LogDecisionRequested(eventDefinition);
            ClearPopupChoices();
            foreach (var option in eventDefinition.Options)
            {
                var capturedOptionId = option.OptionId;
                CreatePopupChoice(new PopupChoice
                {
                    Label = option.Label,
                    OnSelected = () =>
                    {
                        suppressRefresh = true;
                        session.ResolvePlayerEvent(eventDefinition, capturedOptionId);
                        ClosePopupIfOpen();
                        suppressRefresh = false;
                        RefreshPresentation();
                    }
                });
            }

            if (!popupOwnsPause)
            {
                session.PushPauseOverride();
                popupOwnsPause = true;
            }
        }

        private void ClosePopupIfOpen()
        {
            popupOverlay.SetActive(false);
            ClearPopupChoices();
            if (popupOwnsPause)
            {
                session.PopPauseOverride();
                popupOwnsPause = false;
            }
        }

        private void ClearPopupChoices()
        {
            if (popupChoiceContainer == null)
            {
                return;
            }

            for (var index = popupChoiceContainer.childCount - 1; index >= 0; index--)
            {
                Destroy(popupChoiceContainer.GetChild(index).gameObject);
            }
        }

        private void CreatePopupChoice(PopupChoice choice)
        {
            CreateButton(popupChoiceContainer, choice.Label, choice.Label, choice.OnSelected);
        }

        private string GetCurrentSeasonName()
        {
            var seasons = scenario.CalendarDefinition != null ? scenario.CalendarDefinition.Seasons : null;
            if (seasons == null || seasons.Count == 0 || session.RunState?.Calendar == null)
            {
                return "Season";
            }

            var index = Mathf.Clamp(session.RunState.Calendar.SeasonIndex, 0, seasons.Count - 1);
            return seasons[index];
        }

        private string FormatLocationState(CharacterState characterState)
        {
            var current = GetLocationDisplayName(characterState.CurrentLocationId);
            if (!characterState.IsTravelling)
            {
                return current;
            }

            return $"{current} → {GetLocationDisplayName(characterState.TravelDestinationLocationId)}";
        }

        private string GetLocationDisplayName(string locationId)
        {
            var location = worldRegistry.FindLocation(locationId);
            return location != null ? location.DisplayName : string.IsNullOrEmpty(locationId) ? "Unknown" : locationId;
        }

        private string FormatWorldFlags()
        {
            if (session.RunState == null || session.RunState.WorldFlags.Count == 0)
            {
                return "None";
            }

            var builder = new StringBuilder();
            var first = true;
            foreach (var flag in session.RunState.WorldFlags)
            {
                if (!first)
                {
                    builder.Append('\n');
                }

                first = false;
                builder.Append(flag);
            }

            return builder.ToString();
        }

        private string FormatResourceLines(IReadOnlyDictionary<string, int> resources)
        {
            if (resources == null || resources.Count == 0)
            {
                return "None";
            }

            var builder = new StringBuilder();
            var first = true;
            foreach (var resource in resources)
            {
                if (resource.Value <= 0)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append('\n');
                }

                first = false;
                builder.Append(session.GetResourceDisplayName(resource.Key));
                builder.Append(": ");
                builder.Append(resource.Value);
            }

            return first ? "None" : builder.ToString();
        }

        private static string FormatDuration(float seconds)
        {
            var totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
            var minutes = totalSeconds / 60;
            var remainingSeconds = totalSeconds % 60;
            return $"{minutes:00}:{remainingSeconds:00}";
        }

        private Color GetPhaseColor(ActionPhaseType phaseType)
        {
            switch (phaseType)
            {
                case ActionPhaseType.Work:
                    return new Color(0.87f, 0.23f, 0.23f, 1f);
                case ActionPhaseType.ReturnTravel:
                    return new Color(0.24f, 0.52f, 0.94f, 1f);
                default:
                    return new Color(0.24f, 0.82f, 0.36f, 1f);
            }
        }

        private Image CreateWorkRing(Transform parent, string name)
        {
            var canvasObject = new GameObject(name);
            canvasObject.transform.SetParent(parent, false);
            canvasObject.transform.localPosition = Vector3.zero;
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 64f;
            var rect = canvas.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0.9f, 0.9f);
            var imageObject = new GameObject("Ring");
            imageObject.transform.SetParent(canvasObject.transform, false);
            var imageRect = imageObject.AddComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            var image = imageObject.AddComponent<Image>();
            image.sprite = PrototypeSpriteLibrary.GetCircleSprite();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Radial360;
            image.fillOrigin = (int)Image.Origin360.Top;
            image.fillClockwise = false;
            image.fillAmount = 0f;
            image.gameObject.SetActive(false);
            return image;
        }

        private static void SetAnchoredHorizontal(RectTransform rect, float min, float max)
        {
            rect.anchorMin = new Vector2(min, 0f);
            rect.anchorMax = new Vector2(max, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return rect;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, string text, int fontSize, FontStyle style, TextAnchor alignment, Color color)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var label = rect.gameObject.AddComponent<Text>();
            label.font = uiFont;
            label.text = text;
            label.fontSize = fontSize;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            return label;
        }

        private (Button Button, Text Label, Image Background) CreateButton(Transform parent, string name, string text, Action onClick)
        {
            var rect = CreateRect(name, parent);
            rect.sizeDelta = new Vector2(0f, 76f);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.20f, 0.27f, 0.98f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var label = CreateText("Label", rect, Vector2.zero, Vector2.one, new Vector2(12f, 8f), new Vector2(-12f, -18f), text, 16, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            return (button, label, image);
        }
    }
}
