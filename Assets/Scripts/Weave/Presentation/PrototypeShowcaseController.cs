using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
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
        private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
        private const float TargetAspectRatio = 16f / 9f;

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

        private sealed class PopupChoice
        {
            public string Label;
            public Action OnSelected;
        }

        private sealed class TaskButtonView
        {
            public TaskDefinition Task;
            public Button Button;
            public Text Label;
        }

        private readonly Dictionary<string, SpriteRenderer> characterMarkers = new Dictionary<string, SpriteRenderer>();
        private readonly List<UnityEngine.Object> runtimeDefinitions = new List<UnityEngine.Object>();
        private readonly List<TaskButtonView> taskButtons = new List<TaskButtonView>();

        private PrototypeGameSession session;
        private PlayerCanonState playerCanon = new PlayerCanonState();
        private CharacterDefinition controlledCharacter;
        private List<LocationDefinition> locations = new List<LocationDefinition>();
        private List<CharacterDefinition> characters = new List<CharacterDefinition>();
        private List<TaskDefinition> tasks = new List<TaskDefinition>();
        private EventDefinition playerEvent;
        private EventDefinition npcEvent;
        private Sprite squareSprite;
        private Sprite circleSprite;
        private GameObject runtimeVisualRoot;
        private Font uiFont;
        private string statusMessage = "The prototype now runs on realtime day progression with timed travel, tasks, and modal events.";
        private List<string> seasonNames = new List<string>();
        private bool popupOwnsPause;

        private Canvas runtimeCanvas;
        private RectTransform compositionRoot;
        private Text dayText;
        private Text timeRemainingText;
        private Text controlledCharacterText;
        private Text currentLocationText;
        private Text resourcesText;
        private Text worldFlagsText;
        private Text currentTaskText;
        private Text taskStateText;
        private Text taskTimeText;
        private Text statusText;
        private RectTransform taskButtonContainer;
        private Image taskProgressFill;
        private Image pauseButtonImage;
        private Image playButtonImage;
        private Image fastForwardButtonImage;
        private GameObject popupOverlay;
        private Text popupTitleText;
        private Text popupSourceText;
        private Text popupBodyText;
        private RectTransform popupChoiceContainer;

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
            session.StateChanged += RefreshPresentation;
            session.SimulationAdvanced += HandleSimulationAdvanced;
            session.StartRun(controlledCharacter);

            ConfigureCamera();
            BuildRuntimeVisuals();
            BuildRuntimeUi();
            ApplyFixedAspect();
            UpdateCharacterMarkers();
            RefreshPresentation();
        }

        private void Update()
        {
            ApplyFixedAspect();
            UpdateCharacterMarkers();
        }

        private void OnDestroy()
        {
            if (session != null)
            {
                session.StateChanged -= RefreshPresentation;
                session.SimulationAdvanced -= HandleSimulationAdvanced;
            }

            if (runtimeVisualRoot != null)
            {
                DestroyObject(runtimeVisualRoot);
            }

            if (runtimeCanvas != null)
            {
                DestroyObject(runtimeCanvas.gameObject);
            }

            DestroySprite(squareSprite);
            DestroySprite(circleSprite);

            foreach (var runtimeDefinition in runtimeDefinitions)
            {
                DestroyObject(runtimeDefinition);
            }

            runtimeDefinitions.Clear();
        }

        private void ConfigureCamera()
        {
            if (Camera.main == null)
            {
                return;
            }

            Camera.main.orthographic = true;
            Camera.main.orthographicSize = 5.4f;
            Camera.main.transform.position = new Vector3(0f, 0f, -10f);
            Camera.main.backgroundColor = new Color(0.04f, 0.05f, 0.07f, 1f);
        }

        private void BuildRuntimeVisuals()
        {
            squareSprite = CreateSquareSprite();
            circleSprite = CreateCircleSprite(32);
            runtimeVisualRoot = new GameObject("Prototype Showcase Runtime Visuals");
            characterMarkers.Clear();

            foreach (var location in locations)
            {
                var locationObject = new GameObject($"Location - {location.DisplayName}");
                locationObject.transform.SetParent(runtimeVisualRoot.transform, false);
                locationObject.transform.position = new Vector3(location.MapPosition.x, location.MapPosition.y, 0f);

                var renderer = locationObject.AddComponent<SpriteRenderer>();
                renderer.sprite = squareSprite;
                renderer.color = GetLocationColor(location.LocationType);
                renderer.sortingOrder = 0;
                locationObject.transform.localScale = new Vector3(1.15f, 0.75f, 1f);

                CreateTextLabel(locationObject.transform, location.DisplayName, new Vector3(0f, 0.78f, 0f), 0.22f, Color.white);
            }

            foreach (var character in characters)
            {
                var characterObject = new GameObject($"Character - {character.DisplayName}");
                characterObject.transform.SetParent(runtimeVisualRoot.transform, false);

                var renderer = characterObject.AddComponent<SpriteRenderer>();
                renderer.sprite = circleSprite;
                renderer.color = character.MapColor;
                renderer.sortingOrder = 2;
                characterObject.transform.localScale = new Vector3(0.34f, 0.34f, 1f);

                CreateTextLabel(characterObject.transform, character.DisplayName, new Vector3(0f, -0.55f, 0f), 0.16f, character.MapColor);
                characterMarkers[character.CharacterId] = renderer;
            }
        }

        private void BuildRuntimeUi()
        {
            uiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            EnsureEventSystem();

            var canvasObject = new GameObject("Prototype Showcase Canvas");
            runtimeCanvas = canvasObject.AddComponent<Canvas>();
            runtimeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            runtimeCanvas.sortingOrder = 100;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();

            var canvasRect = runtimeCanvas.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            compositionRoot = CreateRect("Composition Root", canvasRect);
            compositionRoot.anchorMin = new Vector2(0.5f, 0.5f);
            compositionRoot.anchorMax = new Vector2(0.5f, 0.5f);
            compositionRoot.pivot = new Vector2(0.5f, 0.5f);
            compositionRoot.sizeDelta = ReferenceResolution;
            var fitter = compositionRoot.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = TargetAspectRatio;

            var frame = CreatePanel(
                "Frame",
                compositionRoot,
                Vector2.zero,
                Vector2.one,
                new Vector2(24f, 24f),
                new Vector2(-24f, -24f),
                new Color(0.08f, 0.10f, 0.13f, 0.94f));
            frame.gameObject.AddComponent<Outline>().effectColor = new Color(0.38f, 0.43f, 0.50f, 0.9f);

            CreatePanel(
                "Map Frame",
                frame,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(108f, 168f),
                new Vector2(-108f, -254f),
                new Color(0.02f, 0.03f, 0.04f, 0.08f));

            CreateText(
                "Map Title",
                frame,
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(-220f, -120f),
                new Vector2(220f, -70f),
                "VILLAGE MAP",
                38,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);

            var infoPanel = CreatePanel(
                "Info Panel",
                frame,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(42f, -42f),
                new Vector2(510f, -290f),
                new Color(0.12f, 0.15f, 0.19f, 0.88f));

            controlledCharacterText = CreateText(
                "Controlled Character",
                infoPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -18f),
                new Vector2(-18f, -56f),
                string.Empty,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            currentLocationText = CreateText(
                "Current Location",
                infoPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -60f),
                new Vector2(-18f, -96f),
                string.Empty,
                20,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.84f, 0.88f, 0.94f, 1f));

            resourcesText = CreateText(
                "Resources",
                infoPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -102f),
                new Vector2(-18f, -152f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.84f, 0.88f, 0.94f, 1f));

            worldFlagsText = CreateText(
                "World Flags",
                infoPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(18f, 18f),
                new Vector2(-18f, 76f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.LowerLeft,
                new Color(0.73f, 0.79f, 0.86f, 1f));

            var clockPanel = CreatePanel(
                "Clock Panel",
                frame,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-540f, -42f),
                new Vector2(-42f, -180f),
                new Color(0.12f, 0.15f, 0.19f, 0.88f));

            var clockButtonRow = CreateRect("Clock Buttons", clockPanel);
            clockButtonRow.anchorMin = new Vector2(0f, 1f);
            clockButtonRow.anchorMax = new Vector2(1f, 1f);
            clockButtonRow.offsetMin = new Vector2(16f, -70f);
            clockButtonRow.offsetMax = new Vector2(-16f, -16f);
            var clockLayout = clockButtonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            clockLayout.spacing = 12f;
            clockLayout.childForceExpandWidth = true;
            clockLayout.childForceExpandHeight = true;
            clockLayout.childControlWidth = true;
            clockLayout.childControlHeight = true;

            pauseButtonImage = CreateButton(clockButtonRow, "Pause", "||", () => session.SetSimulationSpeed(SimulationSpeedMode.Paused)).Image;
            playButtonImage = CreateButton(clockButtonRow, "Play", ">", () => session.SetSimulationSpeed(SimulationSpeedMode.Normal)).Image;
            fastForwardButtonImage = CreateButton(clockButtonRow, "Fast Forward", ">>", () => session.SetSimulationSpeed(SimulationSpeedMode.FastForward)).Image;

            dayText = CreateText(
                "Day Text",
                clockPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(18f, 54f),
                new Vector2(-18f, 94f),
                string.Empty,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleRight,
                Color.white);

            timeRemainingText = CreateText(
                "Time Remaining Text",
                clockPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(18f, 12f),
                new Vector2(-18f, 52f),
                string.Empty,
                28,
                FontStyle.Normal,
                TextAnchor.MiddleRight,
                new Color(0.95f, 0.97f, 1f, 1f));

            var eventButtonPanel = CreatePanel(
                "Event Button Panel",
                frame,
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(42f, -310f),
                new Vector2(510f, -480f),
                new Color(0.12f, 0.15f, 0.19f, 0.88f));

            CreateText(
                "Event Actions Label",
                eventButtonPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -18f),
                new Vector2(-18f, -48f),
                "Event Actions",
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            var eventButtonLayout = CreateRect("Event Buttons", eventButtonPanel);
            eventButtonLayout.anchorMin = new Vector2(0f, 0f);
            eventButtonLayout.anchorMax = new Vector2(1f, 1f);
            eventButtonLayout.offsetMin = new Vector2(18f, 18f);
            eventButtonLayout.offsetMax = new Vector2(-18f, -58f);
            var eventLayout = eventButtonLayout.gameObject.AddComponent<VerticalLayoutGroup>();
            eventLayout.spacing = 10f;
            eventLayout.childControlHeight = true;
            eventLayout.childControlWidth = true;
            eventLayout.childForceExpandHeight = false;
            eventLayout.childForceExpandWidth = true;

            CreateButton(eventButtonLayout, "Village Request", "Open Village Request", ShowPlayerEventPopup);
            CreateButton(eventButtonLayout, "Resolve Rowan Canon Event", "Resolve Rowan Canon Event", ResolveNpcEvent);
            CreateButton(eventButtonLayout, "Restart Demo Run", "Restart Demo Run", RestartRun);

            var bottomPanel = CreatePanel(
                "Bottom Panel",
                frame,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(42f, 42f),
                new Vector2(-42f, 226f),
                new Color(0.12f, 0.15f, 0.19f, 0.92f));

            var taskListPanel = CreatePanel(
                "Task List Panel",
                bottomPanel,
                new Vector2(0f, 0f),
                new Vector2(0.48f, 1f),
                new Vector2(18f, 18f),
                new Vector2(-12f, -18f),
                new Color(0.10f, 0.13f, 0.17f, 0.92f));

            CreateText(
                "Task List Label",
                taskListPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(16f, -16f),
                new Vector2(-16f, -48f),
                "Available Assignments",
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            taskButtonContainer = CreateRect("Task Buttons", taskListPanel);
            taskButtonContainer.anchorMin = new Vector2(0f, 0f);
            taskButtonContainer.anchorMax = new Vector2(1f, 1f);
            taskButtonContainer.offsetMin = new Vector2(16f, 16f);
            taskButtonContainer.offsetMax = new Vector2(-16f, -56f);
            var taskLayout = taskButtonContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            taskLayout.spacing = 10f;
            taskLayout.childControlHeight = true;
            taskLayout.childControlWidth = true;
            taskLayout.childForceExpandHeight = false;
            taskLayout.childForceExpandWidth = true;
            taskButtonContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var task in tasks)
            {
                var capturedTask = task;
                var buttonView = CreateButton(taskButtonContainer, task.DisplayName, task.DisplayName, () => AssignTask(capturedTask));
                taskButtons.Add(new TaskButtonView
                {
                    Task = capturedTask,
                    Button = buttonView.Button,
                    Label = buttonView.Label
                });
            }

            var activeTaskPanel = CreatePanel(
                "Active Task Panel",
                bottomPanel,
                new Vector2(0.48f, 0f),
                new Vector2(1f, 1f),
                new Vector2(12f, 18f),
                new Vector2(-18f, -18f),
                new Color(0.10f, 0.13f, 0.17f, 0.92f));

            CreateText(
                "Active Task Label",
                activeTaskPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(16f, -16f),
                new Vector2(-16f, -48f),
                "Current Task",
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            currentTaskText = CreateText(
                "Current Task Text",
                activeTaskPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(16f, -52f),
                new Vector2(-16f, -88f),
                string.Empty,
                24,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                new Color(0.96f, 0.89f, 0.57f, 1f));

            taskStateText = CreateText(
                "Task State Text",
                activeTaskPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(16f, -92f),
                new Vector2(-16f, -126f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.83f, 0.88f, 0.94f, 1f));

            var progressBackground = CreatePanel(
                "Task Progress Background",
                activeTaskPanel,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(16f, -10f),
                new Vector2(-16f, 24f),
                new Color(0.20f, 0.24f, 0.29f, 1f));
            taskProgressFill = CreatePanel(
                "Task Progress Fill",
                progressBackground,
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                Vector2.zero,
                Vector2.zero,
                new Color(0.35f, 0.74f, 0.48f, 1f))
                .GetComponent<Image>();

            taskTimeText = CreateText(
                "Task Time Text",
                activeTaskPanel,
                new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(16f, -54f),
                new Vector2(-16f, -18f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleRight,
                new Color(0.95f, 0.97f, 1f, 1f));

            statusText = CreateText(
                "Status Text",
                activeTaskPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(16f, 16f),
                new Vector2(-16f, 92f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.81f, 0.86f, 0.92f, 1f));
            statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            statusText.verticalOverflow = VerticalWrapMode.Overflow;

            popupOverlay = CreatePanel(
                "Popup Overlay",
                compositionRoot,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                new Color(0f, 0f, 0f, 0.55f))
                .gameObject;
            popupOverlay.SetActive(false);

            var popupPanel = CreatePanel(
                "Popup Panel",
                popupOverlay.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(-340f, -220f),
                new Vector2(340f, 220f),
                new Color(0.13f, 0.16f, 0.20f, 0.98f));
            popupPanel.gameObject.AddComponent<Outline>().effectColor = new Color(0.52f, 0.58f, 0.66f, 0.9f);

            popupTitleText = CreateText(
                "Popup Title",
                popupPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(24f, -24f),
                new Vector2(-24f, -72f),
                string.Empty,
                30,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            popupSourceText = CreateText(
                "Popup Source",
                popupPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(24f, -74f),
                new Vector2(-24f, -106f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.MiddleLeft,
                new Color(0.83f, 0.88f, 0.94f, 1f));

            popupBodyText = CreateText(
                "Popup Body",
                popupPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(24f, 120f),
                new Vector2(-24f, -116f),
                string.Empty,
                24,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.95f, 0.97f, 1f, 1f));
            popupBodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            popupBodyText.verticalOverflow = VerticalWrapMode.Overflow;

            popupChoiceContainer = CreateRect("Popup Choices", popupPanel);
            popupChoiceContainer.anchorMin = new Vector2(0f, 0f);
            popupChoiceContainer.anchorMax = new Vector2(1f, 0f);
            popupChoiceContainer.offsetMin = new Vector2(24f, 24f);
            popupChoiceContainer.offsetMax = new Vector2(-24f, 104f);
            var popupLayout = popupChoiceContainer.gameObject.AddComponent<VerticalLayoutGroup>();
            popupLayout.spacing = 12f;
            popupLayout.childControlHeight = true;
            popupLayout.childControlWidth = true;
            popupLayout.childForceExpandHeight = false;
            popupLayout.childForceExpandWidth = true;
            popupChoiceContainer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshPresentation()
        {
            if (session == null || session.RunState == null || controlledCharacter == null)
            {
                return;
            }

            var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
            controlledCharacterText.text = $"{controlledCharacter.DisplayName} ({controlledCharacter.Profession})";
            currentLocationText.text = $"Location: {GetLocationDisplayName(controlledState.CurrentLocationId)}";
            resourcesText.text = $"Resources: {FormatResources(controlledState)}";
            worldFlagsText.text = $"World Flags: {FormatWorldFlags()}";
            dayText.text = $"{GetCurrentSeasonName()} — Day {session.RunState.Calendar.DayOfSeason}";
            timeRemainingText.text = $"{FormatDuration(session.RunState.DayTimer.RemainingSeconds)} Remaining";
            currentTaskText.text = GetTaskDisplayName(controlledState.CurrentTaskId);
            taskStateText.text = GetTaskStateText(controlledState);
            taskTimeText.text = GetTaskTimeText(controlledState);
            statusText.text = statusMessage;

            var progress = controlledState.IsWorkingOnTask && controlledState.TaskDurationSeconds > 0f
                ? Mathf.Clamp01(controlledState.TaskElapsedSeconds / controlledState.TaskDurationSeconds)
                : 0f;
            taskProgressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            taskProgressFill.rectTransform.offsetMin = Vector2.zero;
            taskProgressFill.rectTransform.offsetMax = Vector2.zero;
            taskProgressFill.gameObject.SetActive(progress > 0f);

            RefreshTaskButtons(controlledState);
            RefreshSpeedButtons();
        }

        private void RefreshTaskButtons(CharacterState controlledState)
        {
            var availableTaskIds = new HashSet<string>();

            if (!controlledState.HasActiveTask)
            {
                foreach (var task in session.GetPlayerTasks())
                {
                    availableTaskIds.Add(task.TaskId);
                }
            }

            foreach (var taskButton in taskButtons)
            {
                var available = availableTaskIds.Contains(taskButton.Task.TaskId) && !controlledState.HasActiveTask;
                taskButton.Button.interactable = available;
                taskButton.Label.text = available
                    ? $"{taskButton.Task.DisplayName} ({Mathf.RoundToInt(taskButton.Task.DurationSeconds)}s)"
                    : $"{taskButton.Task.DisplayName} ({Mathf.RoundToInt(taskButton.Task.DurationSeconds)}s)";
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
                ? new Color(0.35f, 0.74f, 0.48f, 1f)
                : selected
                    ? new Color(0.28f, 0.38f, 0.52f, 1f)
                    : new Color(0.18f, 0.22f, 0.27f, 1f);
        }

        private void HandleSimulationAdvanced(SimulationAdvanceResult result)
        {
            if (result.TaskInterrupted)
            {
                statusMessage = $"{GetTaskDisplayName(result.InterruptedTaskId)} was interrupted when the day ended.";
            }
            else if (result.TaskCompleted)
            {
                var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
                statusMessage = $"{GetTaskDisplayName(result.CompletedTaskId)} completed. {controlledCharacter.DisplayName} now has {FormatResources(controlledState)}.";

                if (result.CompletedTaskFollowUpEvent != null)
                {
                    ShowDecisionPopup(result.CompletedTaskFollowUpEvent);
                }
            }

            if (result.DayAdvanced && !result.TaskInterrupted && !result.TaskCompleted)
            {
                statusMessage = $"A new day has begun: {GetCurrentSeasonName()} Day {session.RunState.Calendar.DayOfSeason}.";
            }
        }

        private void RestartRun()
        {
            ClosePopupIfOpen();
            playerCanon = new PlayerCanonState();
            session.StartRun(controlledCharacter);
            statusMessage = "Restarted the run from day one with the authored starting data.";
            RefreshPresentation();
        }

        private void AssignTask(TaskDefinition task)
        {
            if (task == null || session.RunState == null)
            {
                return;
            }

            var command = session.AssignPlayerTask(task);

            if (string.IsNullOrEmpty(command.CharacterId))
            {
                return;
            }

            statusMessage = $"{controlledCharacter.DisplayName} is travelling to {task.RequiredLocation.DisplayName} for {task.DisplayName}.";
            RefreshPresentation();
        }

        private void ShowPlayerEventPopup()
        {
            ShowDecisionPopup(playerEvent);
        }

        private void ResolveNpcEvent()
        {
            var resolution = session.ResolveNpcEvent(playerCanon, npcEvent);
            statusMessage = string.IsNullOrEmpty(resolution.SummaryText)
                ? "Resolved Rowan's event."
                : resolution.SummaryText;
            RefreshPresentation();
        }

        private void ShowDecisionPopup(EventDefinition eventDefinition)
        {
            if (eventDefinition == null || popupOverlay == null || popupOverlay.activeSelf)
            {
                return;
            }

            popupOverlay.SetActive(true);
            popupTitleText.text = GetEventTitle(eventDefinition);
            popupSourceText.text = GetEventSourceLabel(eventDefinition);
            popupSourceText.gameObject.SetActive(!string.IsNullOrEmpty(popupSourceText.text));
            popupBodyText.text = eventDefinition.Prompt;
            ClearPopupChoices();

            foreach (var option in eventDefinition.Options)
            {
                var capturedOptionId = option.OptionId;
                var capturedLabel = option.Label;
                CreatePopupChoice(new PopupChoice
                {
                    Label = capturedLabel,
                    OnSelected = () =>
                    {
                        var resolution = session.ResolvePlayerEvent(eventDefinition, capturedOptionId);
                        statusMessage = string.IsNullOrEmpty(resolution.SummaryText)
                            ? $"Resolved {GetEventTitle(eventDefinition)}."
                            : resolution.SummaryText;
                        ClosePopupIfOpen();
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
            if (popupOverlay != null)
            {
                popupOverlay.SetActive(false);
            }

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
                DestroyObject(popupChoiceContainer.GetChild(index).gameObject);
            }
        }

        private void CreatePopupChoice(PopupChoice choice)
        {
            CreateButton(popupChoiceContainer, choice.Label, choice.Label, choice.OnSelected);
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

        private void ApplyFixedAspect()
        {
            if (Camera.main == null)
            {
                return;
            }

            var screenAspect = (float)Screen.width / Mathf.Max(Screen.height, 1);

            if (Mathf.Approximately(screenAspect, TargetAspectRatio))
            {
                Camera.main.rect = new Rect(0f, 0f, 1f, 1f);
                return;
            }

            if (screenAspect > TargetAspectRatio)
            {
                var width = TargetAspectRatio / screenAspect;
                Camera.main.rect = new Rect((1f - width) * 0.5f, 0f, width, 1f);
                return;
            }

            var height = screenAspect / TargetAspectRatio;
            Camera.main.rect = new Rect(0f, (1f - height) * 0.5f, 1f, height);
        }

        private ShowcaseScenario CreateScenario()
        {
            runtimeDefinitions.Clear();
            var scenario = new ShowcaseScenario();
            scenario.Calendar = Track(CreateCalendar());

            var villageSquare = Track(CreateLocation("village_square", "Village Square", LocationType.Village, new Vector2(0f, 0f)));
            var easternMine = Track(CreateLocation("eastern_mine", "Mine", LocationType.Mine, new Vector2(4f, 1.8f)));
            var pineForest = Track(CreateLocation("pine_forest", "Forest", LocationType.Forest, new Vector2(-3.8f, -1.7f)));
            var riversideFarm = Track(CreateLocation("riverside_farm", "Farm", LocationType.Farm, new Vector2(-4f, 1.8f)));
            var oldWorkshop = Track(CreateLocation("old_workshop", "Smithy", LocationType.Workshop, new Vector2(2.6f, -1.7f)));

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
                    "Mining Iron",
                    easternMine,
                    25f,
                    new List<CharacterDefinition> { mina },
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "iron", Amount = 2 } })),
                Track(CreateTask(
                    "gather_timber",
                    "Gather Timber",
                    pineForest,
                    20f,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "wood", Amount = 3 } })),
                Track(CreateTask(
                    "inspect_workshop",
                    "Inspect Workshop",
                    oldWorkshop,
                    18f,
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
            SerializedFieldUtility.SetPrivateField(calendar, "dayDurationSeconds", 300f);
            SerializedFieldUtility.SetPrivateField(calendar, "normalSimulationSpeed", 1f);
            SerializedFieldUtility.SetPrivateField(calendar, "fastForwardSimulationSpeed", 3f);
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
            float durationSeconds,
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
            SerializedFieldUtility.SetPrivateField(task, "durationSeconds", durationSeconds);
            SerializedFieldUtility.SetPrivateField(task, "actorResourceChanges", actorResourceChanges);
            SerializedFieldUtility.SetPrivateField(task, "followUpEvent", null);
            return task;
        }

        private static EventDefinition CreatePlayerEvent(CharacterDefinition mina)
        {
            var eventDefinition = ScriptableObject.CreateInstance<EventDefinition>();
            SerializedFieldUtility.SetPrivateField(eventDefinition, "eventId", "village_request");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "title", "ALICE NEEDS HELP");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "prompt", "Alice asks if you can help repair the damaged fence near the workshop.");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "sourceLabel", "Alice");
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
                        "Help Alice",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "Mina spends time helping with the fence and earns goodwill around the village.",
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
                        "Refuse",
                        new List<OutcomeVariantDefinition>
                        {
                            CreateOutcome(
                                "Mina refuses and keeps her attention on the day's work instead.",
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
            SerializedFieldUtility.SetPrivateField(eventDefinition, "title", "ROWAN DECIDES");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "prompt", "Rowan decides whether to share workshop supplies with the village.");
            SerializedFieldUtility.SetPrivateField(eventDefinition, "sourceLabel", "Rowan");
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
                                "Because Mina helped earlier, Rowan shares workshop supplies with the village.",
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
                                "Rowan considers sharing, but without earlier support he keeps the supplies at the workshop.",
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
                                "The player-canon override keeps Rowan focused on the workshop today.",
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

        private string GetCurrentSeasonName()
        {
            var seasonIndex = session.RunState.Calendar.SeasonIndex;

            if (seasonIndex < 0 || seasonIndex >= seasonNames.Count)
            {
                return "Unknown";
            }

            return seasonNames[seasonIndex];
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

        private static string GetTaskStateText(CharacterState characterState)
        {
            if (characterState.IsWorkingOnTask)
            {
                return "Working";
            }

            if (characterState.IsTravelling)
            {
                return "Travelling";
            }

            return "Idle";
        }

        private string GetTaskTimeText(CharacterState characterState)
        {
            if (characterState.IsWorkingOnTask)
            {
                var remaining = Mathf.Max(0f, characterState.TaskDurationSeconds - characterState.TaskElapsedSeconds);
                return $"{Mathf.CeilToInt(remaining)}s remaining";
            }

            if (characterState.IsTravelling)
            {
                var destination = GetLocationDisplayName(characterState.TravelDestinationLocationId);
                return $"En route to {destination}";
            }

            return "No active assignment";
        }

        private string GetEventTitle(EventDefinition eventDefinition)
        {
            if (!string.IsNullOrEmpty(eventDefinition.Title))
            {
                return eventDefinition.Title;
            }

            return string.IsNullOrEmpty(eventDefinition.EventId) ? "Decision" : eventDefinition.EventId;
        }

        private string GetEventSourceLabel(EventDefinition eventDefinition)
        {
            if (!string.IsNullOrEmpty(eventDefinition.SourceLabel))
            {
                return eventDefinition.SourceLabel;
            }

            return eventDefinition.DecisionMaker != null ? eventDefinition.DecisionMaker.DisplayName : string.Empty;
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

        private static string FormatDuration(float totalSeconds)
        {
            var clamped = Mathf.Max(0, Mathf.CeilToInt(totalSeconds));
            var minutes = clamped / 60;
            var seconds = clamped % 60;
            return $"{minutes:00}:{seconds:00}";
        }

        private static Sprite CreateSquareSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
        }

        private static Sprite CreateCircleSprite(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var radius = (size - 1) * 0.5f;
            var center = new Vector2(radius, radius);

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var alpha = Vector2.Distance(new Vector2(x, y), center) <= radius ? 1f : 0f;
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        private RectTransform CreateRect(string name, Transform parent)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            gameObject.transform.SetParent(parent, false);
            return gameObject.GetComponent<RectTransform>();
        }

        private RectTransform CreatePanel(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            Color color)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = squareSprite;
            image.type = Image.Type.Sliced;
            image.color = color;
            return rect;
        }

        private Text CreateText(
            string name,
            Transform parent,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax,
            string text,
            int fontSize,
            FontStyle fontStyle,
            TextAnchor alignment,
            Color color)
        {
            var rect = CreateRect(name, parent);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;

            var uiText = rect.gameObject.AddComponent<Text>();
            uiText.font = uiFont;
            uiText.fontSize = fontSize;
            uiText.fontStyle = fontStyle;
            uiText.alignment = alignment;
            uiText.color = color;
            uiText.text = text;
            return uiText;
        }

        private (Button Button, Image Image, Text Label) CreateButton(
            Transform parent,
            string name,
            string label,
            Action onClick)
        {
            var rect = CreateRect(name, parent);
            rect.sizeDelta = new Vector2(0f, 50f);

            var layoutElement = rect.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = 50f;

            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = squareSprite;
            image.color = new Color(0.18f, 0.22f, 0.27f, 1f);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => onClick?.Invoke());

            var labelText = CreateText(
                $"{name} Label",
                rect,
                Vector2.zero,
                Vector2.one,
                new Vector2(8f, 4f),
                new Vector2(-8f, -4f),
                label,
                20,
                FontStyle.Bold,
                TextAnchor.MiddleCenter,
                Color.white);

            return (button, image, labelText);
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

        private static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            DestroyObject(sprite.texture);
            DestroyObject(sprite);
        }

        private T Track<T>(T target)
            where T : UnityEngine.Object
        {
            runtimeDefinitions.Add(target);
            return target;
        }
    }
}
