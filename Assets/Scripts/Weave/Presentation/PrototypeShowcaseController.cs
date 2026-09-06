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
        private const float MapPadding = 54f;
        private const int ActivityHistoryLimit = 50;

        [Serializable]
        private sealed class ProgressPhaseTheme
        {
            public Color TravelPreparationColor = new Color(0.24f, 0.82f, 0.36f, 1f);
            public Color WorkColor = new Color(0.87f, 0.23f, 0.23f, 1f);
            public Color ReturnTravelColor = new Color(0.24f, 0.52f, 0.94f, 1f);
            public Color DepositColor = new Color(0.94f, 0.84f, 0.22f, 1f);
            public Color OverallProgressColor = new Color(0.24f, 0.30f, 0.36f, 0.65f);
            public Color PhaseBarBaseColor = new Color(0.09f, 0.11f, 0.14f, 1f);
        }

        private sealed class ShowcaseScenario
        {
            public GameCalendarDefinition Calendar;
            public CharacterDefinition ControlledCharacter;
            public List<LocationDefinition> Locations;
            public List<CharacterDefinition> Characters;
            public List<TaskDefinition> Tasks;
            public List<ResourceDefinition> Resources;
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
            public RectTransform PhaseContainer;
            public Image OverallFill;
            public Image ForegroundFill;
            public Text Label;
        }

        private readonly Dictionary<string, RectTransform> mapLocationNodes = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, RectTransform> characterMarkers = new Dictionary<string, RectTransform>();
        private readonly Dictionary<string, Image> workRings = new Dictionary<string, Image>();
        private readonly Dictionary<string, Text> locationCoordinateTexts = new Dictionary<string, Text>();
        private readonly List<UnityEngine.Object> runtimeDefinitions = new List<UnityEngine.Object>();
        private readonly List<TaskButtonView> taskButtons = new List<TaskButtonView>();
        private readonly HashSet<string> loggedDecisionRequests = new HashSet<string>();

        private PrototypeGameSession session;
        [SerializeField] private ProgressPhaseTheme phaseTheme = new ProgressPhaseTheme();
        private PlayerCanonState playerCanon = new PlayerCanonState();
        private CharacterDefinition controlledCharacter;
        private List<LocationDefinition> locations = new List<LocationDefinition>();
        private List<CharacterDefinition> characters = new List<CharacterDefinition>();
        private List<TaskDefinition> tasks = new List<TaskDefinition>();
        private EventDefinition playerEvent;
        private EventDefinition npcEvent;
        private List<ResourceDefinition> resources = new List<ResourceDefinition>();
        private Sprite squareSprite;
        private Sprite circleSprite;
        private GameObject runtimeVisualRoot;
        private Font uiFont;
        private readonly List<SimulationLogEntry> activityLogEntries = new List<SimulationLogEntry>();
        private List<string> seasonNames = new List<string>();
        private bool popupOwnsPause;
        private bool suppressPresentationRefresh;

        private Canvas runtimeCanvas;
        private RectTransform compositionRoot;
        private RectTransform mapViewport;
        private RectTransform mapContent;
        private Text dayText;
        private Text timeRemainingText;
        private Text controlledCharacterText;
        private Text currentLocationText;
        private Text resourcesText;
        private Text carryingText;
        private Text worldFlagsText;
        private ScrollRect activityScrollRect;
        private RectTransform activityConsoleContent;
        private Text activityConsoleText;
        private RectTransform taskButtonContainer;
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
            resources = scenario.Resources;
            playerEvent = scenario.PlayerEvent;
            npcEvent = scenario.NpcEvent;
            seasonNames = new List<string>(scenario.Calendar.Seasons);

            session.Configure(scenario.Calendar, locations, characters, tasks, resources);
            ConfigureCamera();
            BuildRuntimeVisuals();
            BuildRuntimeUi();
            BuildMapUi();
            session.StateChanged += RefreshPresentation;
            session.SimulationAdvanced += HandleSimulationAdvanced;
            session.SimulationLogEntryAdded += HandleSimulationLogEntryAdded;
            session.StartRun(controlledCharacter);
            ApplyFixedAspect();
            UpdateCharacterMarkers();
            RebuildActivityConsoleFromSession();
            RefreshPresentation();
        }

        private void Update()
        {
            ApplyFixedAspect();
            UpdateCharacterMarkers();
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
            runtimeVisualRoot.SetActive(false);
            mapLocationNodes.Clear();
            locationCoordinateTexts.Clear();
            characterMarkers.Clear();
            workRings.Clear();
        }

        private void BuildRuntimeUi()
        {
            uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

            mapViewport = CreatePanel(
                "Map Frame",
                frame,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(108f, 168f),
                new Vector2(-108f, -254f),
                new Color(0.05f, 0.07f, 0.10f, 0.85f));
            mapViewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            mapContent = CreateRect("Map Content", mapViewport);
            mapContent.anchorMin = Vector2.zero;
            mapContent.anchorMax = Vector2.one;
            mapContent.offsetMin = Vector2.zero;
            mapContent.offsetMax = Vector2.zero;

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
                new Vector2(-18f, -146f),
                string.Empty,
                18,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.84f, 0.88f, 0.94f, 1f));

            carryingText = CreateText(
                "Carrying",
                infoPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(18f, -150f),
                new Vector2(-18f, -224f),
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
                new Vector2(-18f, 118f),
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
                var phaseContainer = CreatePanel(
                    $"{task.DisplayName} Phase Bar",
                    buttonView.Button.transform,
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(10f, 6f),
                    new Vector2(-10f, 16f),
                    phaseTheme.PhaseBarBaseColor);
                phaseContainer.GetComponent<Image>().raycastTarget = false;
                phaseContainer.SetAsFirstSibling();

                var overallFill = CreatePanel(
                    $"{task.DisplayName} Overall Fill",
                    phaseContainer,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    Vector2.zero,
                    Vector2.zero,
                    phaseTheme.OverallProgressColor).GetComponent<Image>();
                overallFill.raycastTarget = false;

                var foregroundFill = CreatePanel(
                    $"{task.DisplayName} Foreground Fill",
                    phaseContainer,
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    Vector2.zero,
                    Vector2.zero,
                    phaseTheme.TravelPreparationColor).GetComponent<Image>();
                foregroundFill.raycastTarget = false;
                taskButtons.Add(new TaskButtonView
                {
                    Task = capturedTask,
                    Button = buttonView.Button,
                    PhaseContainer = phaseContainer,
                    OverallFill = overallFill,
                    ForegroundFill = foregroundFill,
                    Label = buttonView.Label
                });
            }

            var activityPanel = CreatePanel(
                "Activity Panel",
                bottomPanel,
                new Vector2(0.48f, 0f),
                new Vector2(1f, 1f),
                new Vector2(12f, 18f),
                new Vector2(-18f, -18f),
                new Color(0.10f, 0.13f, 0.17f, 0.92f));

            CreateText(
                "Activity Label",
                activityPanel,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(16f, -16f),
                new Vector2(-16f, -48f),
                "ACTIVITY",
                22,
                FontStyle.Bold,
                TextAnchor.MiddleLeft,
                Color.white);

            var activityViewport = CreatePanel(
                "Activity Viewport",
                activityPanel,
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(16f, 16f),
                new Vector2(-16f, -52f),
                new Color(0.08f, 0.11f, 0.15f, 0.92f));
            var viewportMask = activityViewport.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            activityScrollRect = activityViewport.gameObject.AddComponent<ScrollRect>();
            activityScrollRect.horizontal = false;
            activityScrollRect.vertical = true;
            activityScrollRect.movementType = ScrollRect.MovementType.Clamped;
            activityScrollRect.scrollSensitivity = 20f;
            activityScrollRect.viewport = activityViewport;

            activityConsoleContent = CreateRect("Activity Console Content", activityViewport);
            activityConsoleContent.anchorMin = new Vector2(0f, 1f);
            activityConsoleContent.anchorMax = new Vector2(1f, 1f);
            activityConsoleContent.pivot = new Vector2(0.5f, 1f);
            activityConsoleContent.offsetMin = new Vector2(0f, 0f);
            activityConsoleContent.offsetMax = new Vector2(0f, 0f);
            var contentSizeFitter = activityConsoleContent.gameObject.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            contentSizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            activityScrollRect.content = activityConsoleContent;

            activityConsoleText = CreateText(
                "Activity Console Text",
                activityConsoleContent,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(10f, -10f),
                new Vector2(-10f, -10f),
                string.Empty,
                17,
                FontStyle.Normal,
                TextAnchor.UpperLeft,
                new Color(0.87f, 0.92f, 0.98f, 1f));
            activityConsoleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            activityConsoleText.verticalOverflow = VerticalWrapMode.Overflow;
            activityConsoleText.supportRichText = false;
            var textRect = activityConsoleText.rectTransform;
            textRect.pivot = new Vector2(0.5f, 1f);
            var textFitter = activityConsoleText.gameObject.AddComponent<ContentSizeFitter>();
            textFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            textFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

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

        private void BuildMapUi()
        {
            mapLocationNodes.Clear();
            locationCoordinateTexts.Clear();
            characterMarkers.Clear();
            workRings.Clear();

            if (mapContent == null)
            {
                return;
            }

            foreach (var location in locations)
            {
                var locationRect = CreatePanel(
                    $"Map Location - {location.DisplayName}",
                    mapContent,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(-40f, -24f),
                    new Vector2(40f, 24f),
                    GetLocationColor(location.LocationType));
                locationRect.localScale = Vector3.one;
                mapLocationNodes[location.LocationId] = locationRect;

                var nameText = CreateText(
                    $"{location.DisplayName} Name",
                    locationRect,
                    new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f),
                    new Vector2(-120f, 10f),
                    new Vector2(120f, 44f),
                    location.DisplayName.ToUpperInvariant(),
                    16,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    Color.white);
                nameText.raycastTarget = false;

                var coordinateText = CreateText(
                    $"{location.DisplayName} Coords",
                    locationRect,
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(-120f, -40f),
                    new Vector2(120f, -12f),
                    $"({location.MapPosition.x:0.#}, {location.MapPosition.y:0.#})",
                    14,
                    FontStyle.Normal,
                    TextAnchor.MiddleCenter,
                    new Color(0.87f, 0.91f, 0.96f, 1f));
                coordinateText.raycastTarget = false;
                locationCoordinateTexts[location.LocationId] = coordinateText;
            }

            foreach (var character in characters)
            {
                var markerRect = CreateRect($"Character - {character.DisplayName}", mapContent);
                markerRect.anchorMin = new Vector2(0.5f, 0.5f);
                markerRect.anchorMax = new Vector2(0.5f, 0.5f);
                markerRect.sizeDelta = new Vector2(24f, 24f);

                var markerImage = markerRect.gameObject.AddComponent<Image>();
                markerImage.sprite = circleSprite;
                markerImage.color = character.MapColor;
                markerImage.raycastTarget = false;

                var ringRect = CreateRect("Work Ring", markerRect);
                ringRect.anchorMin = new Vector2(0.5f, 0.5f);
                ringRect.anchorMax = new Vector2(0.5f, 0.5f);
                ringRect.sizeDelta = new Vector2(34f, 34f);
                ringRect.anchoredPosition = Vector2.zero;
                var ringImage = ringRect.gameObject.AddComponent<Image>();
                ringImage.sprite = circleSprite;
                ringImage.type = Image.Type.Filled;
                ringImage.fillMethod = Image.FillMethod.Radial360;
                ringImage.fillOrigin = (int)Image.Origin360.Top;
                ringImage.fillClockwise = false;
                ringImage.fillAmount = 0f;
                ringImage.color = phaseTheme.WorkColor;
                ringImage.raycastTarget = false;
                ringImage.gameObject.SetActive(false);

                CreateText(
                    $"{character.DisplayName} Label",
                    markerRect,
                    new Vector2(0.5f, 0f),
                    new Vector2(0.5f, 0f),
                    new Vector2(-120f, -34f),
                    new Vector2(120f, -8f),
                    character.DisplayName,
                    13,
                    FontStyle.Bold,
                    TextAnchor.MiddleCenter,
                    character.MapColor).raycastTarget = false;

                characterMarkers[character.CharacterId] = markerRect;
                workRings[character.CharacterId] = ringImage;
            }

            RefreshMapLayout();
        }

        private void RefreshPresentation()
        {
            if (session == null ||
                session.RunState == null ||
                controlledCharacter == null ||
                dayText == null ||
                timeRemainingText == null ||
                suppressPresentationRefresh)
            {
                return;
            }

            var controlledState = session.RunState.GetCharacter(controlledCharacter.CharacterId);
            var actionProgress = session.GetActionProgressForCharacter(controlledCharacter.CharacterId);
            controlledCharacterText.text = $"{controlledCharacter.DisplayName} ({controlledCharacter.Profession})";
            currentLocationText.text = GetCurrentLocationText(controlledState);
            resourcesText.text = $"Stored: {FormatResources(controlledState.StoredResources)}";
            carryingText.text = $"Carrying: {FormatResources(controlledState.CarriedResources)}\nCarry Weight: {session.GetCharacterCarriedWeight(controlledCharacter.CharacterId):0.##}";
            worldFlagsText.text = $"World Flags: {FormatWorldFlags()}";
            dayText.text = $"{GetCurrentSeasonName()} — Day {session.RunState.Calendar.DayOfSeason}";
            timeRemainingText.text = $"{FormatDuration(session.RunState.DayTimer.RemainingSeconds)} Remaining";

            RefreshTaskButtons(controlledState, actionProgress);
            RefreshSpeedButtons();
        }

        private void RefreshTaskButtons(CharacterState controlledState, ActionProgressSummary activeActionProgress)
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
                var isSelectedTask = controlledState.HasActiveTask && controlledState.CurrentTaskId == taskButton.Task.TaskId;
                var progress = isSelectedTask
                    ? activeActionProgress
                    : session.GetTaskPlanPreview(controlledCharacter.CharacterId, taskButton.Task);
                var travelOrPrep = GetTravelHintForTask(controlledState, taskButton.Task, progress);
                var suffix = controlledState.HasActiveTask
                    ? controlledState.CurrentTaskId == taskButton.Task.TaskId
                        ? string.Empty
                        : " • Busy"
                    : availableTaskIds.Contains(taskButton.Task.TaskId) ? string.Empty : " • Unavailable";
                var statusLine = isSelectedTask
                    ? GetActiveTaskStatusLine(progress, taskButton.Task)
                    : $"Travel: {travelOrPrep}";
                if (!string.IsNullOrEmpty(suffix))
                {
                    statusLine = $"{statusLine}{suffix}";
                }

                taskButton.Label.alignment = TextAnchor.UpperLeft;
                taskButton.Label.text = $"{taskButton.Task.DisplayName} ({Mathf.RoundToInt(taskButton.Task.DurationSeconds)}s)\n{statusLine}";
                RefreshTaskPhaseBar(taskButton, progress, isSelectedTask);
            }
        }

        private void RefreshSpeedButtons()
        {
            RefreshSpeedButton(pauseButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.Paused, session.EffectiveSpeedMode == SimulationSpeedMode.Paused);
            RefreshSpeedButton(playButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.Normal, session.EffectiveSpeedMode == SimulationSpeedMode.Normal);
            RefreshSpeedButton(fastForwardButtonImage, session.SelectedSpeedMode == SimulationSpeedMode.FastForward, session.EffectiveSpeedMode == SimulationSpeedMode.FastForward);
        }

        private void RefreshTaskPhaseBar(TaskButtonView taskButton, ActionProgressSummary progress, bool showProgress)
        {
            if (taskButton.PhaseContainer == null)
            {
                return;
            }

            if (!progress.HasPhases || progress.TotalDurationSeconds <= Mathf.Epsilon)
            {
                taskButton.PhaseContainer.gameObject.SetActive(false);
                return;
            }

            taskButton.PhaseContainer.gameObject.SetActive(true);
            var overallProgress = showProgress ? progress.OverallProgress : 0f;
            var currentPhaseProgress = showProgress ? progress.CurrentPhaseProgress : 0f;
            var currentPhaseColor = GetPhaseColor(progress.CurrentPhase.PhaseType);

            taskButton.OverallFill.color = phaseTheme.OverallProgressColor;
            taskButton.ForegroundFill.color = currentPhaseColor;
            SetAnchoredHorizontal(taskButton.OverallFill.rectTransform, 0f, overallProgress);
            SetAnchoredHorizontal(taskButton.ForegroundFill.rectTransform, 0f, currentPhaseProgress);
            taskButton.OverallFill.gameObject.SetActive(overallProgress > 0f);
            taskButton.ForegroundFill.gameObject.SetActive(showProgress && currentPhaseProgress > 0f);
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
            if (result.TaskCompleted && result.CompletedTaskFollowUpEvent != null)
            {
                ShowDecisionPopup(result.CompletedTaskFollowUpEvent);
            }
        }

        private void HandleSimulationLogEntryAdded(SimulationLogEntry entry)
        {
            activityLogEntries.Add(entry);
            while (activityLogEntries.Count > ActivityHistoryLimit)
            {
                activityLogEntries.RemoveAt(0);
            }

            RefreshActivityConsoleText();
        }

        private void RebuildActivityConsoleFromSession()
        {
            activityLogEntries.Clear();
            if (session == null)
            {
                return;
            }

            foreach (var entry in session.SimulationLogEntries)
            {
                activityLogEntries.Add(entry);
            }

            while (activityLogEntries.Count > ActivityHistoryLimit)
            {
                activityLogEntries.RemoveAt(0);
            }

            RefreshActivityConsoleText();
        }

        private void RefreshActivityConsoleText()
        {
            if (activityConsoleText == null)
            {
                return;
            }

            var builder = new StringBuilder();
            for (var index = 0; index < activityLogEntries.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(FormatActivityEntry(activityLogEntries[index]));
            }

            activityConsoleText.text = builder.ToString();
            Canvas.ForceUpdateCanvases();

            if (activityScrollRect != null)
            {
                activityScrollRect.verticalNormalizedPosition = 0f;
            }
        }

        private string FormatActivityEntry(SimulationLogEntry entry)
        {
            var timestamp = $"D{entry.DayOfSeason:00} {FormatDuration(entry.DayElapsedSeconds)}";
            return $"{timestamp} — [{entry.Category.ToString().ToUpperInvariant()}] {entry.Message}";
        }

        private void RestartRun()
        {
            ClosePopupIfOpen();
            playerCanon = new PlayerCanonState();
            loggedDecisionRequests.Clear();
            session.StartRun(controlledCharacter);
            RebuildActivityConsoleFromSession();
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
            RefreshPresentation();
        }

        private void ShowPlayerEventPopup()
        {
            ShowDecisionPopup(playerEvent);
        }

        private void ResolveNpcEvent()
        {
            session.ResolveNpcEvent(playerCanon, npcEvent);
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
            var decisionMaker = eventDefinition.DecisionMaker != null ? eventDefinition.DecisionMaker.CharacterId : controlledCharacter.CharacterId;
            var eventRequestKey = string.IsNullOrEmpty(eventDefinition.EventId)
                ? GetEventTitle(eventDefinition)
                : eventDefinition.EventId;
            if (!loggedDecisionRequests.Contains(eventRequestKey))
            {
                loggedDecisionRequests.Add(eventRequestKey);
                var sourceLabel = string.IsNullOrEmpty(popupSourceText.text) ? "System" : popupSourceText.text;
                session.PublishSimulationLog(
                    SimulationLogCategory.Event,
                    decisionMaker,
                    $"{sourceLabel} requested a decision: {GetEventTitle(eventDefinition)}.");
            }
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
                        suppressPresentationRefresh = true;
                        session.ResolvePlayerEvent(eventDefinition, capturedOptionId);
                        ClosePopupIfOpen();
                        suppressPresentationRefresh = false;
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

            RefreshMapLayout();

            foreach (var character in characters)
            {
                if (!characterMarkers.TryGetValue(character.CharacterId, out var marker))
                {
                    continue;
                }

                var position = session.GetCharacterMapPosition(character.CharacterId);
                marker.anchoredPosition = GetMapAnchoredPosition(position);
                UpdateWorkRing(character.CharacterId);
            }
        }

        private void RefreshMapLayout()
        {
            if (mapViewport == null || mapContent == null)
            {
                return;
            }

            foreach (var location in locations)
            {
                if (!mapLocationNodes.TryGetValue(location.LocationId, out var node))
                {
                    continue;
                }

                node.anchoredPosition = GetMapAnchoredPosition(location.MapPosition);
            }
        }

        private Vector2 GetMapAnchoredPosition(Vector2 logicalPosition)
        {
            if (mapViewport == null)
            {
                return Vector2.zero;
            }

            var extents = GetLogicalMapExtents();
            var halfWidth = Mathf.Max(16f, mapViewport.rect.width * 0.5f - MapPadding);
            var halfHeight = Mathf.Max(16f, mapViewport.rect.height * 0.5f - MapPadding);
            var normalizedX = logicalPosition.x / extents.x;
            var normalizedY = logicalPosition.y / extents.y;
            return new Vector2(
                Mathf.Clamp(normalizedX, -1f, 1f) * halfWidth,
                Mathf.Clamp(normalizedY, -1f, 1f) * halfHeight);
        }

        private Vector2 GetLogicalMapExtents()
        {
            var maxX = 1f;
            var maxY = 1f;

            foreach (var location in locations)
            {
                maxX = Mathf.Max(maxX, Mathf.Abs(location.MapPosition.x));
                maxY = Mathf.Max(maxY, Mathf.Abs(location.MapPosition.y));
            }

            return new Vector2(maxX, maxY);
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
            scenario.Resources = new List<ResourceDefinition>
            {
                Track(CreateResource("iron", "Iron Ore", 2f)),
                Track(CreateResource("wood", "Wood", 1f)),
                Track(CreateResource("coal", "Coal", 1f)),
                Track(CreateResource("goodwill", "Goodwill", 0f)),
                Track(CreateResource("tools", "Tools", 1f)),
                Track(CreateResource("grain", "Grain", 1f))
            };

            var home = Track(CreateLocation("home", "Home", LocationType.Home, new Vector2(0f, 0f)));
            var easternMine = Track(CreateLocation("eastern_mine", "Mine", LocationType.Mine, new Vector2(0f, 3f)));
            var pineForest = Track(CreateLocation("pine_forest", "Forest", LocationType.Forest, new Vector2(-3f, 0f)));
            var riversideFarm = Track(CreateLocation("riverside_farm", "Farm", LocationType.Farm, new Vector2(0f, -3f)));
            var oldWorkshop = Track(CreateLocation("old_workshop", "Smithy", LocationType.Workshop, new Vector2(3f, 0f)));

            scenario.Locations = new List<LocationDefinition>
            {
                home,
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
                home,
                new List<ResourceAmount>
                {
                    new ResourceAmount { ResourceId = "iron", Amount = 0 },
                    new ResourceAmount { ResourceId = "wood", Amount = 0 },
                    new ResourceAmount { ResourceId = "coal", Amount = 0 },
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
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "iron", Amount = 5 } },
                    true,
                    false,
                    false)),
                Track(CreateTask(
                    "gather_timber",
                    "Gather Timber",
                    pineForest,
                    20f,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "wood", Amount = 3 } },
                    true,
                    false,
                    false)),
                Track(CreateTask(
                    "inspect_workshop",
                    "Inspect Workshop",
                    oldWorkshop,
                    18f,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount> { new ResourceAmount { ResourceId = "goodwill", Amount = 1 } },
                    false,
                    false,
                    false)),
                Track(CreateTask(
                    "return_home",
                    "Return Home",
                    home,
                    0f,
                    new List<CharacterDefinition>(),
                    new List<ResourceAmount>(),
                    false,
                    true,
                    true))
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

        private static ResourceDefinition CreateResource(string id, string displayName, float carryWeightPerUnit)
        {
            var resource = ScriptableObject.CreateInstance<ResourceDefinition>();
            SerializedFieldUtility.SetPrivateField(resource, "resourceId", id);
            SerializedFieldUtility.SetPrivateField(resource, "displayName", displayName);
            SerializedFieldUtility.SetPrivateField(resource, "carryWeightPerUnit", carryWeightPerUnit);
            return resource;
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
            List<ResourceAmount> actorResourceChanges,
            bool rewardsAddedToCarriedResources,
            bool completeOnArrival,
            bool unavailableWhenAlreadyAtRequiredLocation)
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
            SerializedFieldUtility.SetPrivateField(task, "rewardsAddedToCarriedResources", rewardsAddedToCarriedResources);
            SerializedFieldUtility.SetPrivateField(task, "completeOnArrival", completeOnArrival);
            SerializedFieldUtility.SetPrivateField(task, "unavailableWhenAlreadyAtRequiredLocation", unavailableWhenAlreadyAtRequiredLocation);
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

        private string GetCurrentLocationText(CharacterState characterState)
        {
            var current = $"Current Location: {GetLocationDisplayName(characterState.CurrentLocationId)}";

            if (!characterState.IsTravelling)
            {
                return current;
            }

            var origin = GetLocationDisplayName(characterState.TravelOriginLocationId);
            var destination = GetLocationDisplayName(characterState.TravelDestinationLocationId);
            return $"{current}\nTravelling: {origin} -> {destination}";
        }

        private string GetActiveTaskStatusLine(ActionProgressSummary actionProgress, TaskDefinition task)
        {
            if (!actionProgress.HasPhases || actionProgress.CurrentPhaseIndex < 0)
            {
                return "Idle";
            }

            var remainingSeconds = Mathf.CeilToInt(actionProgress.CurrentPhaseRemainingSeconds);
            switch (actionProgress.CurrentPhase.PhaseType)
            {
                case ActionPhaseType.TravelPreparation:
                    return $"Preparing... {remainingSeconds}s remaining";
                case ActionPhaseType.Work:
                    return $"{GetWorkVerb(task)}... {remainingSeconds}s remaining";
                case ActionPhaseType.ReturnTravel:
                    return $"Returning Home... {remainingSeconds}s remaining";
                case ActionPhaseType.Deposit:
                    return $"Depositing... {remainingSeconds}s remaining";
                default:
                    return $"In progress... {remainingSeconds}s remaining";
            }
        }

        private string GetTravelHintForTask(CharacterState controlledState, TaskDefinition task, ActionProgressSummary progress)
        {
            if (task == null || task.RequiredLocation == null)
            {
                return "No location";
            }

            var travelPhase = progress.HasPhases ? progress.Phases[0] : default;
            var travelSeconds = Mathf.CeilToInt(travelPhase.DurationSeconds);
            var isReturnTask = travelPhase.PhaseType == ActionPhaseType.ReturnTravel;

            if (isReturnTask)
            {
                var carryWeight = session.GetCharacterCarriedWeight(controlledCharacter.CharacterId);
                return $"{travelSeconds}s return / {carryWeight:0.#} wt";
            }

            var sameLocation = controlledState.CurrentLocationId == task.RequiredLocation.LocationId;
            return sameLocation
                ? $"Here / {travelSeconds}s prep"
                : $"{travelSeconds}s travel";
        }

        private static string GetWorkVerb(TaskDefinition task)
        {
            if (task == null || string.IsNullOrEmpty(task.DisplayName))
            {
                return "Working";
            }

            return task.DisplayName.StartsWith("Gather", StringComparison.OrdinalIgnoreCase)
                ? "Gathering"
                : task.DisplayName.StartsWith("Mining", StringComparison.OrdinalIgnoreCase)
                    ? "Mining"
                    : task.DisplayName.StartsWith("Inspect", StringComparison.OrdinalIgnoreCase)
                        ? "Inspecting"
                        : "Working";
        }

        private void UpdateWorkRing(string characterId)
        {
            if (!workRings.TryGetValue(characterId, out var ring) || session?.RunState == null)
            {
                return;
            }

            var characterState = session.RunState.GetCharacter(characterId);
            var actionProgress = session.GetActionProgressForCharacter(characterId);

            if (!characterState.IsWorkingOnTask || characterState.TaskDurationSeconds <= 0f)
            {
                ring.gameObject.SetActive(false);
                return;
            }

            if (!actionProgress.HasPhases || actionProgress.CurrentPhase.PhaseType != ActionPhaseType.Work)
            {
                ring.gameObject.SetActive(false);
                return;
            }

            ring.fillAmount = actionProgress.CurrentPhaseProgress;
            ring.color = phaseTheme.WorkColor;
            ring.gameObject.SetActive(true);
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

        private string FormatResources(IReadOnlyDictionary<string, int> resourcesById)
        {
            var builder = new StringBuilder();
            var first = true;

            foreach (var resource in resourcesById)
            {
                if (resource.Value <= 0)
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(", ");
                }

                builder.Append(session.GetResourceDisplayName(resource.Key));
                builder.Append(':');
                builder.Append(' ');
                builder.Append(resource.Value);
                first = false;
            }

            return first ? "None" : builder.ToString();
        }

        private static void SetAnchoredHorizontal(RectTransform rectTransform, float minX, float maxX)
        {
            rectTransform.anchorMin = new Vector2(minX, 0f);
            rectTransform.anchorMax = new Vector2(maxX, 1f);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private Color GetPhaseColor(ActionPhaseType phaseType)
        {
            switch (phaseType)
            {
                case ActionPhaseType.TravelPreparation:
                    return phaseTheme.TravelPreparationColor;
                case ActionPhaseType.Work:
                    return phaseTheme.WorkColor;
                case ActionPhaseType.ReturnTravel:
                    return phaseTheme.ReturnTravelColor;
                case ActionPhaseType.Deposit:
                    return phaseTheme.DepositColor;
                default:
                    return Color.white;
            }
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
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
    eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
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
                case LocationType.Home:
                    return new Color(0.31f, 0.44f, 0.66f, 1f);
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

        private void OnDestroy()
        {
            if (session != null)
            {
                session.StateChanged -= RefreshPresentation;
                session.SimulationAdvanced -= HandleSimulationAdvanced;
                session.SimulationLogEntryAdded -= HandleSimulationLogEntryAdded;
            }

            if (runtimeVisualRoot != null)
            {
                DestroyUnityObject(runtimeVisualRoot);
            }

            if (runtimeCanvas != null)
            {
                DestroyUnityObject(runtimeCanvas.gameObject);
            }

            DestroySprite(squareSprite);
            DestroySprite(circleSprite);

            foreach (var runtimeDefinition in runtimeDefinitions)
            {
                DestroyUnityObject(runtimeDefinition);
            }

            runtimeDefinitions.Clear();
        }
        private static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
            {
                return;
            }

            DestroyUnityObject(sprite.texture);
            DestroyUnityObject(sprite);
        }


        private void ClearPopupChoices()
        {
            if (popupChoiceContainer == null)
            {
                return;
            }

            for (var index = popupChoiceContainer.childCount - 1; index >= 0; index--)
            {
                DestroyUnityObject(popupChoiceContainer.GetChild(index).gameObject);
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(target);
                return;
            }

            UnityEngine.Object.DestroyImmediate(target);
        }
        private T Track<T>(T target)
            where T : UnityEngine.Object
        {
            runtimeDefinitions.Add(target);
            return target;
        }
    }
}
