using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuntimeCurveEditor;
using UnityEngine;
using UnityEngine.UIElements;

[DefaultExecutionOrder(-100)]
public class InGameSettingsMenu : MonoBehaviour
{
    public enum TabType
    {
        Scene,
        PostProcessing,
    }

    private bool isRefreshingSuppressed = false;

    private UIDocument uiDocument;

    /// <summary>
    /// Get the SceneController instance dynamically
    /// </summary>
    private SceneController Controller => SceneController.Instance;

    private VisualElement settingsPanel;
    private ScrollView sceneSettingsPanel;
    private ScrollView postProcessingPanel;
    private DropdownField sceneProfileDropdown,
        postProcessingProfileDropdown;
    private Button sceneLoadButton,
        sceneSaveButton,
        sceneSaveAsButton;
    private Button postProcessingLoadButton,
        postProcessingSaveButton,
        postProcessingSaveAsButton;
    private Button closeButton;
    private Button sceneTab,
        postProcessingTab;

    private RuntimeSceneSettings runtimeSettings;
    private string currentSceneProfilePath = "";
    private string currentPostProcessingProfilePath = "";

    // Dirty tracking: canonical JSON of the scene / PP slice as it was when the active profile
    // was last loaded or saved. Dirty = current canonical JSON differs from this.
    private string sceneBaselineJson = "";
    private string postProcessingBaselineJson = "";
    private bool isSceneDirty;
    private bool isPostProcessingDirty;
    private Label sceneDirtyLabel;
    private Label postProcessingDirtyLabel;

    // True when Start() restored the working set - suppresses the last-used-profile auto-load.
    private bool restoredFromWorkingSet;
    private string sceneProfilesDirectory;
    private string postProcessingProfilesDirectory;
    private string lastUsedSceneProfileKey = "LastUsedSceneProfile";
    private string lastUsedPostProcessingProfileKey = "LastUsedPostProcessingProfile";

    private readonly List<VisualElement> settingGroups = new();
    private readonly Dictionary<string, VisualElement> settingElements = new();
    private readonly List<Texture2D> curveTextures = new();
    private bool isModalOpen = false;
    private VisualElement curveEditorBlocker;
    private Label tooltipElement;

    public event Action<RuntimeSceneSettings> OnSettingsChanged;

    public bool IsMenuOpen => !settingsPanel.ClassListContains("hidden");

    public enum ProfileType
    {
        Scene,
        PostProcessing,
    }

    private void Awake()
    {
        sceneProfilesDirectory = Path.Combine(
            Application.streamingAssetsPath,
            "SettingsProfiles",
            "Scene"
        );
        postProcessingProfilesDirectory = Path.Combine(
            Application.streamingAssetsPath,
            "SettingsProfiles",
            "PostProcessing"
        );

        if (!Directory.Exists(sceneProfilesDirectory))
        {
            Directory.CreateDirectory(sceneProfilesDirectory);
        }

        if (!Directory.Exists(postProcessingProfilesDirectory))
        {
            Directory.CreateDirectory(postProcessingProfilesDirectory);
        }

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        // Initialize scene-specific keys early
        InitializeSceneSpecificKeysFromController();
    }

    private void Start()
    {
        InitializeRuntimeSettings();
        SetupUI();

        // The working set (latest values from anywhere) wins over the last-used profile.
        restoredFromWorkingSet = TryRestoreWorkingSet();

        RefreshSceneProfiles();
        RefreshPostProcessingProfiles();
        CreateSettingsUI();

        if (restoredFromWorkingSet)
        {
            // Push the restored values to the controller (inspector twins, effective settings,
            // volume) the same way a menu edit would.
            NotifySettingsChanged();
        }
        else
        {
            // Nothing restored: the profile auto-load (or the inspector seed) is the working set now.
            UpdateDirtyState();
            SaveWorkingSet();
        }
        UpdateDirtyIndicators();
    }

    /// <summary>
    /// Initialize scene-specific keys directly from controller if available
    /// </summary>
    private void InitializeSceneSpecificKeysFromController()
    {
        if (Controller != null)
        {
            lastUsedSceneProfileKey = Controller.GetSceneSpecificSceneProfileKey();
            lastUsedPostProcessingProfileKey =
                Controller.GetSceneSpecificPostProcessingProfileKey();
        }
    }

    /// <summary>
    /// Ensure scene-specific keys are updated before using them
    /// </summary>
    private void EnsureSceneSpecificKeys()
    {
        if (
            Controller != null
            && (
                lastUsedSceneProfileKey == "LastUsedSceneProfile"
                || lastUsedPostProcessingProfileKey == "LastUsedPostProcessingProfile"
            )
        )
        {
            lastUsedSceneProfileKey = Controller.GetSceneSpecificSceneProfileKey();
            lastUsedPostProcessingProfileKey =
                Controller.GetSceneSpecificPostProcessingProfileKey();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M) && !isModalOpen && !RuntimeCurveEditorWindow.IsVisible)
        {
            ToggleMenu();
        }

        // Toggle the invisible blocker overlay so UI Toolkit elements can't be
        // interacted with while the IMGUI curve editor is visible.
        if (curveEditorBlocker != null)
        {
            curveEditorBlocker.style.display = RuntimeCurveEditorWindow.IsVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private void InitializeRuntimeSettings()
    {
        if (Controller != null)
        {
            // Get settings from SceneController inspector values
            var seed = new RuntimeSceneSettings();
            Controller.CopyInspectorToRuntime(seed);
            SetRuntimeSettings(seed);
        }
        else
        {
            // Create default runtime settings if controller is not available
            SetRuntimeSettings(new RuntimeSceneSettings());
        }
    }

    private void SetupUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("UIDocument is null, cannot setup UI");
            return;
        }

        var root = uiDocument.rootVisualElement;

        // Invisible overlay that blocks all UI Toolkit interaction when the
        // IMGUI curve editor is open (the two input systems are independent).
        curveEditorBlocker = new VisualElement();
        curveEditorBlocker.style.position = Position.Absolute;
        curveEditorBlocker.style.left = 0;
        curveEditorBlocker.style.top = 0;
        curveEditorBlocker.style.right = 0;
        curveEditorBlocker.style.bottom = 0;
        curveEditorBlocker.pickingMode = PickingMode.Position;
        curveEditorBlocker.style.display = DisplayStyle.None;
        root.Add(curveEditorBlocker);

        // Runtime tooltip popup. UI Toolkit's built-in VisualElement.tooltip only renders
        // inside the Editor, so hover descriptions are drawn with this shared label.
        tooltipElement = new Label();
        tooltipElement.AddToClassList("setting-tooltip");
        tooltipElement.pickingMode = PickingMode.Ignore;
        tooltipElement.style.position = Position.Absolute;
        tooltipElement.style.display = DisplayStyle.None;
        root.Add(tooltipElement);

        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        sceneSettingsPanel = root.Q<ScrollView>("SceneSettingsPanel");
        postProcessingPanel = root.Q<ScrollView>("PostProcessingPanel");

        // Scene tab controls
        var sceneTabContent = root.Q<VisualElement>("SceneTabContent");
        if (sceneTabContent != null)
        {
            sceneProfileDropdown = sceneTabContent.Q<DropdownField>("SceneProfileDropdown");
            sceneDirtyLabel = sceneTabContent.Q<Label>("SceneDirtyLabel");
            sceneLoadButton = sceneTabContent.Q<Button>("SceneLoadButton");
            sceneSaveButton = sceneTabContent.Q<Button>("SceneSaveButton");
            sceneSaveAsButton = sceneTabContent.Q<Button>("SceneSaveAsButton");
        }

        // Post-processing tab controls
        var postProcessingTabContent = root.Q<VisualElement>("PostProcessingTabContent");
        if (postProcessingTabContent != null)
        {
            postProcessingProfileDropdown = postProcessingTabContent.Q<DropdownField>(
                "PostProcessingProfileDropdown"
            );
            postProcessingDirtyLabel = postProcessingTabContent.Q<Label>(
                "PostProcessingDirtyLabel"
            );
            postProcessingLoadButton = postProcessingTabContent.Q<Button>(
                "PostProcessingLoadButton"
            );
            postProcessingSaveButton = postProcessingTabContent.Q<Button>(
                "PostProcessingSaveButton"
            );
            postProcessingSaveAsButton = postProcessingTabContent.Q<Button>(
                "PostProcessingSaveAsButton"
            );
        }

        closeButton = root.Q<Button>("CloseButton");
        sceneTab = root.Q<Button>("SceneTab");
        postProcessingTab = root.Q<Button>("PostProcessingTab");

        // Setup button callbacks
        closeButton.clicked += CloseMenu;

        // Scene tab callbacks
        if (sceneLoadButton != null)
            sceneLoadButton.clicked += () => RequestLoadSelectedProfile(TabType.Scene);
        if (sceneSaveButton != null)
            sceneSaveButton.clicked += () => SaveCurrentProfile(TabType.Scene);
        if (sceneSaveAsButton != null)
            sceneSaveAsButton.clicked += () => ShowSaveAsDialog(TabType.Scene);

        // Post-processing tab callbacks
        if (postProcessingLoadButton != null)
            postProcessingLoadButton.clicked += () =>
                RequestLoadSelectedProfile(TabType.PostProcessing);
        if (postProcessingSaveButton != null)
            postProcessingSaveButton.clicked += () => SaveCurrentProfile(TabType.PostProcessing);
        if (postProcessingSaveAsButton != null)
            postProcessingSaveAsButton.clicked += () => ShowSaveAsDialog(TabType.PostProcessing);

        sceneTab.clicked += () => SwitchTab("scene");
        postProcessingTab.clicked += () => SwitchTab("postprocessing");

        // Auto-load when dropdown selections change
        if (sceneProfileDropdown != null)
        {
            sceneProfileDropdown.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    RequestLoadSelectedProfile(TabType.Scene, evt.previousValue);
                }
            });
        }

        if (postProcessingProfileDropdown != null)
        {
            postProcessingProfileDropdown.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    RequestLoadSelectedProfile(TabType.PostProcessing, evt.previousValue);
                }
            });
        }
    }

    private void CreateSettingsUI()
    {
        if (runtimeSettings == null)
        {
            Debug.LogError("RuntimeSettings is null, cannot create settings UI");
            return;
        }

        if (sceneSettingsPanel == null || postProcessingPanel == null)
        {
            Debug.LogError("UI panels not found, cannot create settings UI");
            return;
        }

        // Destroy tracked curve textures before rebuilding UI
        foreach (var tex in curveTextures)
            if (tex != null)
                Destroy(tex);
        curveTextures.Clear();

        sceneSettingsPanel.Clear();
        postProcessingPanel.Clear();
        settingElements.Clear();

        // Scene Settings Tab
        CreateSceneSettingsContent();

        // Post Processing Tab
        CreatePostProcessingContent();
    }

    private void CreateSceneSettingsContent()
    {
        CreateGravityAttractionGroup(sceneSettingsPanel);
        CreateHandsAttractionGroup(sceneSettingsPanel);
        CreateBoundaryDragGroup(sceneSettingsPanel);
        CreateIntrinsicPulsationGroup(sceneSettingsPanel);
        CreateMovementPulsationGroup(sceneSettingsPanel);
        CreateMiscellaneousGroup(sceneSettingsPanel);
        CreateAnimationGroup(sceneSettingsPanel);
        CreateHandVfxSpawnGroup(sceneSettingsPanel);
        CreateHandVfxCollisionGroup(sceneSettingsPanel);
        CreateHandVfxMainAttractorGroup(sceneSettingsPanel);
        CreateHandVfxTrailDistortersGroup(sceneSettingsPanel);
        CreateHandVfxSecondaryAttractorGroup(sceneSettingsPanel);
        CreateHandVfxNoiseGroup(sceneSettingsPanel);
        CreateHandVfxBurstsGroup(sceneSettingsPanel);
        CreateHandVfxSnareGroup(sceneSettingsPanel);
        CreateStyleGroup(sceneSettingsPanel);
        CreateDebuggingGroup(sceneSettingsPanel);
    }

    private void CreatePostProcessingContent()
    {
        CreatePostProcessingGroup(postProcessingPanel);
    }

    private void SwitchTab(string tabName)
    {
        var root = uiDocument.rootVisualElement;
        var sceneTabContent = root.Q<VisualElement>("SceneTabContent");
        var postProcessingTabContent = root.Q<VisualElement>("PostProcessingTabContent");

        if (tabName == "scene")
        {
            sceneTab.AddToClassList("active");
            postProcessingTab.RemoveFromClassList("active");
            sceneTabContent?.AddToClassList("active");
            postProcessingTabContent?.RemoveFromClassList("active");
        }
        else if (tabName == "postprocessing")
        {
            sceneTab.RemoveFromClassList("active");
            postProcessingTab.AddToClassList("active");
            sceneTabContent?.RemoveFromClassList("active");
            postProcessingTabContent?.AddToClassList("active");
        }
    }

    private void CreateGravityAttractionGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Gravity Attraction", parentContainer);

        CreateFloatField(group, "G (×s²)", () => runtimeSettings.g, v => runtimeSettings.g = v);
        CreateFloatField(
            group,
            "Max Towards Force (×s²)",
            () => runtimeSettings.maxTowardsForce,
            v => runtimeSettings.maxTowardsForce = v,
            tooltip: "Cap on the pairwise gravity force (G*m1*m2/r^2) while two balls are moving toward each other. Keeps close balls from slamming together."
        );
        CreateFloatField(
            group,
            "Max Away Force (×s²)",
            () => runtimeSettings.maxAwayFromForce,
            v => runtimeSettings.maxAwayFromForce = v,
            tooltip: "Cap on the pairwise gravity force while two balls are moving apart. Setting it above Max Towards Force lets gravity resist separation more than it accelerates approach."
        );
        CreateFloatField(
            group,
            "Gravity Force Damper",
            () => runtimeSettings.gravityForceDamper,
            v => runtimeSettings.gravityForceDamper = v,
            tooltip: "Multiplier applied to Max Towards Force when that cap kicks in. Below 1 softens the final approach; 1 = no extra damping."
        );
        CreateFloatField(
            group,
            "Stop Gravity Distance (×s)",
            () => runtimeSettings.stopGravityDistance,
            v => runtimeSettings.stopGravityDistance = v,
            tooltip: "Center-to-center distance below which gravity stops being applied. Inside this range the balls coast (or get stopped, see Stop Moving Distance)."
        );
        CreateFloatField(
            group,
            "Stop Moving Distance (×s)",
            () => runtimeSettings.stopMovingDistance,
            v => runtimeSettings.stopMovingDistance = v,
            tooltip: "Within this center-to-center distance, if the balls' relative speed is below Stop Velocity, a counter-force cancels their motion so they settle side by side."
        );
        CreateFloatField(
            group,
            "Stop Velocity (×s)",
            () => runtimeSettings.stopVelocity,
            v => runtimeSettings.stopVelocity = v,
            tooltip: "Relative speed threshold for the settle-in-place behavior. Balls closer than Stop Moving Distance and slower than this are brought to rest."
        );
        CreateFloatField(
            group,
            "Attraction Radius Multiplier",
            () => runtimeSettings.attractionRadiusMultiplier,
            v => runtimeSettings.attractionRadiusMultiplier = v,
            tooltip: "Scales each ball's attraction radius (relative to its current diameter). Gravity only starts once another ball's body enters this radius. Also sizes the debug radius sprite."
        );
    }

    private void CreateHandsAttractionGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hands Attraction", parentContainer);

        CreateCurveField(
            group,
            "Force To Middle",
            () => runtimeSettings.forceToMiddle,
            v => runtimeSettings.forceToMiddle = v,
            tooltip: "Curve of push-force strength vs. how close the ball is to its target. X: 0 = ball is Max Distance Between Hands away, 1 = ball is at the target. Y multiplies Push Force."
        );
        CreateFloatField(
            group,
            "Single Hand Open Force Damper",
            () => runtimeSettings.singleHandOpenForceDamper,
            v => runtimeSettings.singleHandOpenForceDamper = v,
            tooltip: "Multiplier on Push Force while only one hand is open (0-1). Lets one-handed steering be gentler than two-handed."
        );
        CreateFloatField(
            group,
            "Push Force (×s²)",
            () => runtimeSettings.pushForce,
            v => runtimeSettings.pushForce = v,
            tooltip: "Base rigidbody force driving the ball toward the hand target (midpoint of both hands, or the single open hand). Everything else in this group multiplies it."
        );
        CreateFloatField(
            group,
            "Torso Max Forward Offset (×s)",
            () => runtimeSettings.torsoMaxForwardOffset,
            v => runtimeSettings.torsoMaxForwardOffset = v,
            tooltip: "How far the ball's push target is pulled toward the camera when the hands sit at torso depth. 0 disables."
        );
        CreateFloatField(
            group,
            "Torso Offset Falloff Distance (×s)",
            () => runtimeSettings.torsoOffsetFalloffDistance,
            v => runtimeSettings.torsoOffsetFalloffDistance = v,
            tooltip: "Hand-to-torso z distance at which the torso forward offset fades to zero."
        );
        CreateFloatField(
            group,
            "Min Drag",
            () => runtimeSettings.minDrag,
            v => runtimeSettings.minDrag = v,
            tooltip: "Rigidbody linear damping when the ball is far from the hand target (at Max Distance Between Hands). Lower = ball keeps its momentum longer."
        );
        CreateFloatField(
            group,
            "Max Drag",
            () => runtimeSettings.maxDrag,
            v => runtimeSettings.maxDrag = v,
            tooltip: "Rigidbody linear damping when the ball is right at the hand target. Higher = ball settles quickly instead of overshooting."
        );

        CreateCurveField(
            group,
            "Alignment Vector Strength",
            () => runtimeSettings.alignmentVectorStrength,
            v => runtimeSettings.alignmentVectorStrength = v,
            tooltip: "Curve of how far the target is offset along the direction the hands point (wrist to fingertip). X: 0 = hands together, 1 = hands at Max Distance Between Hands. Y multiplies the scaler below."
        );
        CreateFloatField(
            group,
            "Alignment Vector Strength Scaler (×s)",
            () => runtimeSettings.alignmentVectorStrengthScaler,
            v => runtimeSettings.alignmentVectorStrengthScaler = v,
            tooltip: "Max distance the hand target is pushed along the hands' pointing direction. Lets players aim the ball by tilting their hands rather than only by moving them."
        );
        CreateFloatField(
            group,
            "Hand Push Scaler",
            () => runtimeSettings.handPushScaler,
            v => runtimeSettings.handPushScaler = v,
            tooltip: "Extra multiplier on the push force applied while both hands are closed (the final flick that sends the ball off). Drag is set to 0 during this push."
        );
        CreateToggleField(
            group,
            "Pray To Activate",
            () => runtimeSettings.prayToActivate,
            v => runtimeSettings.prayToActivate = v,
            tooltip: "When enabled, players must bring their hands together to initialize. When disabled, players start initialized."
        );
        CreateFloatField(
            group,
            "Pray To Activate Distance (×s)",
            () => runtimeSettings.prayToActivateDistance,
            v => runtimeSettings.prayToActivateDistance = v,
            tooltip: "The distance (in meters) hands must be within to activate the player when Pray To Activate is enabled."
        );
    }

    private void CreateBoundaryDragGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Boundary Drag", parentContainer);

        CreateFloatField(
            group,
            "Added Boundary Distance (×s)",
            () => runtimeSettings.addedBoundaryDistance,
            v => runtimeSettings.addedBoundaryDistance = v,
            tooltip: "Margin added around the metaball grid to define the play boundary. Beyond it Boundary Outward Drag engages and the ball becomes eligible for reset."
        );
        CreateFloatField(
            group,
            "Boundary Outward Drag (×s)",
            () => runtimeSettings.boundaryOutwardDrag,
            v => runtimeSettings.boundaryOutwardDrag = v,
            tooltip: "Drag that opposes the ball while it is past the boundary and moving away from the hands. 0 disables."
        );
        CreateFloatField(
            group,
            "Out Of Bounds Reset Delay",
            () => runtimeSettings.outOfBoundsResetDelay,
            v => runtimeSettings.outOfBoundsResetDelay = v,
            tooltip: "Seconds the ball must stay out of bounds before opening both hands snaps it back to the hand midpoint (plus Sphere Reset Jitter)."
        );
    }

    private void CreateIntrinsicPulsationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Intrinsic Pulsation", parentContainer);

        CreateSliderField(
            group,
            "Pulse Amount",
            () => runtimeSettings.pulseAmount,
            v => runtimeSettings.pulseAmount = v,
            0f,
            10f,
            tooltip: "Amplitude of the idle 'breathing' size wobble, as a fraction of the ball's size (value/10). 0 disables intrinsic pulsation."
        );
        CreateFloatField(
            group,
            "Pulse Speed",
            () => runtimeSettings.pulseSpeed,
            v => runtimeSettings.pulseSpeed = v,
            tooltip: "Time multiplier for the breathing wobble. Higher = faster oscillation."
        );
        CreateFloatField(
            group,
            "Graph Limit",
            () => runtimeSettings.graphLimit,
            v => runtimeSettings.graphLimit = v,
            tooltip: "Expected peak of the summed sine waves, used to normalize the wobble into 0..Pulse Amount. Roughly the number of Pulse Frequencies entries; lower values clip, higher values flatten the pulse."
        );
        CreateFloatArrayField(
            group,
            "Pulse Frequencies",
            () => runtimeSettings.pulseFreqs,
            v => runtimeSettings.pulseFreqs = v,
            tooltip: "Frequencies of the sine waves summed to make the breathing wobble (y = sin(f1*t) + sin(f2*t) + ...). Mixed, non-integer values give a less regular pulse."
        );
    }

    private void CreateMovementPulsationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Movement-Based Pulsation", parentContainer);

        CreateToggleField(
            group,
            "Single Hand Scaling",
            () => runtimeSettings.singleHandScaling,
            v => runtimeSettings.singleHandScaling = v,
            tooltip: "Allow scaling to occur with only one hand's velocity."
        );
        CreateFloatField(
            group,
            "Minimum Unscaled Size (×s)",
            () => runtimeSettings.minimumUnscaledSize,
            v => runtimeSettings.minimumUnscaledSize = v,
            tooltip: "The minimum size that the vfx body can scale down to."
        );
        CreateFloatField(
            group,
            "Maximum Unscaled Size (×s)",
            () => runtimeSettings.maximumUnscaledSize,
            v => runtimeSettings.maximumUnscaledSize = v,
            tooltip: "The maximum size that the vfx body can scale up to."
        );
        CreateFloatField(
            group,
            "Max Hand Velocity (×s)",
            () => runtimeSettings.maxHandVelocity,
            v => runtimeSettings.maxHandVelocity = v,
            tooltip: "Hand-velocity sanity gate for movement-based scaling: frames where a hand moves faster than this are ignored as tracking glitches."
        );
        CreateSliderField(
            group,
            "Min Hand Displacement Per Frame (×s)",
            () => runtimeSettings.minHandDisplacementPerFrame,
            v => runtimeSettings.minHandDisplacementPerFrame = v,
            0.0001f,
            5f,
            tooltip: "Used to mask false velocity readings due to position jitter from inaccurate sensor readings."
        );
        CreateCurveField(
            group,
            "Distance Damper",
            () => runtimeSettings.distanceDamper,
            v => runtimeSettings.distanceDamper = v,
            tooltip: "Curve scaling the grow/shrink effect by hand separation. X: 0 = hands at Max Distance Between Hands, 1 = hands together. Y multiplies the scale change, so hands close to the ball have more effect."
        );
        CreateFloatField(
            group,
            "Pulse Scale Damper",
            () => runtimeSettings.pulseScaleDamper,
            v => runtimeSettings.pulseScaleDamper = v,
            tooltip: "An overall damper for the movement-based pulsation scaling."
        );
    }

    private void CreateMiscellaneousGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Miscellaneous", parentContainer);

        CreateFloatField(
            group,
            "Merge Size Scaler Damper",
            () => runtimeSettings.mergeSizeScalerDamper,
            v => runtimeSettings.mergeSizeScalerDamper = v,
            tooltip: "A damper for the scaling that occurs when multiple bodies merge together."
        );
        CreateFloatField(
            group,
            "Max Distance Between Hands (×s)",
            () => runtimeSettings.maxDistanceBetweenHands,
            v => runtimeSettings.maxDistanceBetweenHands = v,
            tooltip: "Reference hand separation used to normalize several curves (Force To Middle, Alignment Vector Strength, Distance Damper) and the drag remap. Distances beyond it are clamped."
        );
        CreateFloatField(
            group,
            "Base Z Depth (×s)",
            () => runtimeSettings.baseZDepth,
            v => runtimeSettings.baseZDepth = v,
            tooltip: "World-space depth of the play volume: the metaball grid, boundary and Kinect joints are all placed at this Z. Also sent to the hand VFX."
        );
        CreateFloatField(
            group,
            "Grid Scale (×s)",
            () => runtimeSettings.gridScale,
            v => runtimeSettings.gridScale = v,
            tooltip: "World size of one marching-cubes voxel (volume = 64x32x64 voxels). Scales with bodyScale."
        );
        CreateFloatField(
            group,
            "Default Unscaled Size (×s)",
            () => runtimeSettings.defaultUnscaledSize,
            v => runtimeSettings.defaultUnscaledSize = v,
            tooltip: "Starting diameter of a new player's ball before any pulsation, growing or shrinking is applied."
        );
        CreateFloatField(
            group,
            "Body Scale",
            () => runtimeSettings.bodyScale,
            v => runtimeSettings.bodyScale = v,
            tooltip: "World scale of the Kinect space. Every setting marked (×s), (×s²) or (×1/s) is stored at 1x and multiplied by this at runtime, so changing it alone keeps gameplay and look identical relative to the body."
        );
        CreateFloatField(
            group,
            "Max Distance From Camera (×s)",
            () => runtimeSettings.maxDistanceFromCamera,
            v => runtimeSettings.maxDistanceFromCamera = v,
            tooltip: "If a player's hands are tracked farther from the camera than this, they are treated as closed and their colliders/skeleton lines are disabled (filters people far in the background)."
        );
        CreateFloatField(
            group,
            "Sphere Reset Jitter (×s)",
            () => runtimeSettings.sphereResetJitter,
            v => runtimeSettings.sphereResetJitter = v,
            tooltip: "Random +/- offset added when the ball is reset to the hand midpoint, so overlapping balls don't reset to exactly the same spot."
        );
    }

    private void CreateAnimationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Animation", parentContainer);

        CreateFloatField(
            group,
            "Particle Initialization Delay",
            () => runtimeSettings.particleInitializationDelay,
            v => runtimeSettings.particleInitializationDelay = v,
            tooltip: "The amount of time it takes for the particle initialization animation to play once a new player is added to the scene."
        );
        CreateFloatField(
            group,
            "Initialization Reset Delay",
            () => runtimeSettings.initializationResetDelay,
            v => runtimeSettings.initializationResetDelay = v,
            tooltip: "Seconds a hand-state change must persist before it re-triggers the hand open/close animation, preventing flicker from noisy Kinect hand states."
        );
        CreateSliderField(
            group,
            "Initialization Speed",
            () => runtimeSettings.initializationSpeed,
            v => runtimeSettings.initializationSpeed = v,
            0f,
            1f,
            tooltip: "Speed of the hand opening animation during initialization. Lower values = slower animation."
        );
        CreateFloatField(
            group,
            "Single Hand Open Threshold",
            () => runtimeSettings.singleHandOpenThreshold,
            v => runtimeSettings.singleHandOpenThreshold = v,
            tooltip: "Minimum time in single-hand-open state before the final push uses that hand's position. Accounts for slight timing discrepancies with real Kinect users."
        );
        CreateFloatField(
            group,
            "Single Hand Force Lerp Duration",
            () => runtimeSettings.singleHandForceLerpDuration,
            v => runtimeSettings.singleHandForceLerpDuration = v,
            tooltip: "Seconds to blend the push force from Single Hand Open Force Damper back to full strength after the second hand opens."
        );
        CreateFloatField(
            group,
            "Metaball Radius Animation Duration",
            () => runtimeSettings.metaballRadiusAnimationDuration,
            v => runtimeSettings.metaballRadiusAnimationDuration = v,
            tooltip: "Seconds for the metaball radius to animate from its start size to full size when a player initializes."
        );
        CreateFloatField(
            group,
            "Metaball Radius Animation Start Size (×s)",
            () => runtimeSettings.metaballRadiusAnimationStartSize,
            v => runtimeSettings.metaballRadiusAnimationStartSize = v,
            tooltip: "Starting radius for the metaball grow-in animation when a player initializes."
        );
        CreateCurveField(
            group,
            "Metaball Radius Animation Curve",
            () => runtimeSettings.metaballRadiusAnimationCurve,
            v => runtimeSettings.metaballRadiusAnimationCurve = v,
            tooltip: "Easing curve for the metaball grow-in (X: 0-1 normalized time, Y: 0-1 progress from start size to full size)."
        );
        CreateFloatField(
            group,
            "Body Spawn Size (×s)",
            () => runtimeSettings.bodySpawnSize,
            v => runtimeSettings.bodySpawnSize = v,
            tooltip: "Particle size of the BodyEffects.vfx spawn flash on VFX_Body."
        );
    }

    // ---- Hand VFX (HandVfxSettings, nested in the scene profile as "handVfx") ----
    // Group names mirror the [Header]s in HandVfxSettings. Every dimensioned row shows the
    // base value with a unit hint; the effective value is base × bodyScale^exp.

    private void CreateHandVfxSpawnGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Spawn & Size", parentContainer);

        CreateIntField(
            group,
            "Spawn Rate",
            () => runtimeSettings.handVfx.spawnRate,
            v => runtimeSettings.handVfx.spawnRate = v
        );
        CreateFloatField(
            group,
            "Spawn Sphere Radius (×s)",
            () => runtimeSettings.handVfx.spawnSphereRadius,
            v => runtimeSettings.handVfx.spawnSphereRadius = v
        );
        CreateFloatField(
            group,
            "Spawn Velocity Spread (×s)",
            () => runtimeSettings.handVfx.spawnVeloSpread,
            v => runtimeSettings.handVfx.spawnVeloSpread = v
        );
        CreateVector2Field(
            group,
            "Size Range (×s)",
            () => runtimeSettings.handVfx.sizeRange,
            v => runtimeSettings.handVfx.sizeRange = v
        );
        CreateFloatField(
            group,
            "Lifetime Remap Max Dist (×s)",
            () => runtimeSettings.handVfx.lifetimeRemapMaxDist,
            v => runtimeSettings.handVfx.lifetimeRemapMaxDist = v
        );
        CreateVector2Field(
            group,
            "Life Range (s)",
            () => runtimeSettings.handVfx.lifeRange,
            v => runtimeSettings.handVfx.lifeRange = v
        );
        CreateFloatField(
            group,
            "Length Scaler (×1/s)",
            () => runtimeSettings.handVfx.lengthScaler,
            v => runtimeSettings.handVfx.lengthScaler = v
        );
        CreateFloatField(
            group,
            "Min Stretch Length",
            () => runtimeSettings.handVfx.minStretchLength,
            v => runtimeSettings.handVfx.minStretchLength = v
        );
    }

    private void CreateHandVfxCollisionGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Collision", parentContainer);

        CreateFloatField(
            group,
            "Sphere Collision Scale Mult",
            () => runtimeSettings.handVfx.vfxSphereCollisionScaleMult,
            v => runtimeSettings.handVfx.vfxSphereCollisionScaleMult = v
        );
        CreateFloatField(
            group,
            "Collision Detection Scale Mult",
            () => runtimeSettings.handVfx.collisionDetectionScaleMult,
            v => runtimeSettings.handVfx.collisionDetectionScaleMult = v
        );
    }

    private void CreateHandVfxMainAttractorGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Main Attractor", parentContainer);

        CreateFloatField(
            group,
            "Main Attraction Speed (×s)",
            () => runtimeSettings.handVfx.mainAttractionSpeed,
            v => runtimeSettings.handVfx.mainAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "Main Attraction Force (×s)",
            () => runtimeSettings.handVfx.mainAttractionForce,
            v => runtimeSettings.handVfx.mainAttractionForce = v
        );
        CreateFloatField(
            group,
            "Main Stick Distance (×s)",
            () => runtimeSettings.handVfx.mainStickDistance,
            v => runtimeSettings.handVfx.mainStickDistance = v
        );
        CreateFloatField(
            group,
            "Main Stick Force (×s)",
            () => runtimeSettings.handVfx.mainStickForce,
            v => runtimeSettings.handVfx.mainStickForce = v
        );
        CreateFloatField(
            group,
            "Tangential Damping (1/s)",
            () => runtimeSettings.handVfx.tangentialDamping,
            v => runtimeSettings.handVfx.tangentialDamping = v
        );
        CreateFloatField(
            group,
            "Tangential Damping Cutoff",
            () => runtimeSettings.handVfx.tangentialDampingCutoff,
            v => runtimeSettings.handVfx.tangentialDampingCutoff = v,
            tooltip: "Where tangential damping switches on, as a multiple of the vfxSphere's size. 1 = at its surface, 1.4 = 40% beyond it, below 1 also damps inside the sphere. Closer particles are left alone."
        );
        CreateFloatField(
            group,
            "Seek Strength (×s)",
            () => runtimeSettings.handVfx.seekStrength,
            v => runtimeSettings.handVfx.seekStrength = v
        );
    }

    private void CreateHandVfxTrailDistortersGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Trail Distorters", parentContainer);

        CreateFloatField(
            group,
            "TD Radius (×s)",
            () => runtimeSettings.handVfx.tdRadius,
            v => runtimeSettings.handVfx.tdRadius = v
        );
        CreateFloatField(
            group,
            "TD Stick Distance (×s)",
            () => runtimeSettings.handVfx.tdStickDistance,
            v => runtimeSettings.handVfx.tdStickDistance = v
        );
        CreateFloatField(
            group,
            "TD Stick Force (×s)",
            () => runtimeSettings.handVfx.tdStickForce,
            v => runtimeSettings.handVfx.tdStickForce = v
        );
        CreateFloatField(
            group,
            "TD Attraction Force (×s)",
            () => runtimeSettings.handVfx.tdAttractionForce,
            v => runtimeSettings.handVfx.tdAttractionForce = v
        );
        CreateFloatField(
            group,
            "TD Attraction Speed (×s)",
            () => runtimeSettings.handVfx.tdAttractionSpeed,
            v => runtimeSettings.handVfx.tdAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "TD Wander Amount (×s)",
            () => runtimeSettings.handVfx.tdWanderAmount,
            v => runtimeSettings.handVfx.tdWanderAmount = v
        );
    }

    private void CreateHandVfxSecondaryAttractorGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Secondary Attractor", parentContainer);

        CreateFloatField(
            group,
            "SA Attraction Speed (×s)",
            () => runtimeSettings.handVfx.saAttractionSpeed,
            v => runtimeSettings.handVfx.saAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "SA Attraction Force (×s)",
            () => runtimeSettings.handVfx.saAttractionForce,
            v => runtimeSettings.handVfx.saAttractionForce = v
        );
        CreateFloatField(
            group,
            "SA Stick Distance (×s)",
            () => runtimeSettings.handVfx.saStickDistance,
            v => runtimeSettings.handVfx.saStickDistance = v
        );
        CreateFloatField(
            group,
            "SA Stick Force (×s)",
            () => runtimeSettings.handVfx.saStickForce,
            v => runtimeSettings.handVfx.saStickForce = v
        );
        CreateFloatField(
            group,
            "SA Min Radius (×s)",
            () => runtimeSettings.handVfx.saMinRadius,
            v => runtimeSettings.handVfx.saMinRadius = v
        );
    }

    private void CreateHandVfxNoiseGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Noise & Turbulence", parentContainer);

        CreateFloatField(
            group,
            "Noise Scale (×s)",
            () => runtimeSettings.handVfx.noiseScale,
            v => runtimeSettings.handVfx.noiseScale = v
        );
        CreateFloatField(
            group,
            "Noise Frequency (×1/s)",
            () => runtimeSettings.handVfx.noiseFrequency,
            v => runtimeSettings.handVfx.noiseFrequency = v
        );
        CreateFloatField(
            group,
            "Noise Roughness",
            () => runtimeSettings.handVfx.noiseRoughness,
            v => runtimeSettings.handVfx.noiseRoughness = v
        );
        CreateIntField(
            group,
            "Noise Octaves",
            () => runtimeSettings.handVfx.noiseOctaves,
            v => runtimeSettings.handVfx.noiseOctaves = v
        );
        CreateFloatField(
            group,
            "Turbulence Intensity (×s)",
            () => runtimeSettings.handVfx.turbulenceIntensity,
            v => runtimeSettings.handVfx.turbulenceIntensity = v
        );
        CreateFloatField(
            group,
            "Turbulence Frequency (×1/s)",
            () => runtimeSettings.handVfx.turbulenceFrequency,
            v => runtimeSettings.handVfx.turbulenceFrequency = v
        );
    }

    private void CreateHandVfxBurstsGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Bursts (CHat/OHat)", parentContainer);

        CreateFloatField(
            group,
            "CHat Size (×s)",
            () => runtimeSettings.handVfx.cHatSize,
            v => runtimeSettings.handVfx.cHatSize = v
        );
        CreateFloatField(
            group,
            "CHat Noise Amp (×s)",
            () => runtimeSettings.handVfx.cHatNoiseAmp,
            v => runtimeSettings.handVfx.cHatNoiseAmp = v
        );
        CreateFloatField(
            group,
            "CHat Noise Freq (×1/s)",
            () => runtimeSettings.handVfx.cHatNoiseFreq,
            v => runtimeSettings.handVfx.cHatNoiseFreq = v
        );
        CreateFloatField(
            group,
            "CHat Noise Y Scroll (×s)",
            () => runtimeSettings.handVfx.cHatNoiseYScroll,
            v => runtimeSettings.handVfx.cHatNoiseYScroll = v
        );
        CreateFloatField(
            group,
            "CHat Spawn Velo Sphere Radius (×s)",
            () => runtimeSettings.handVfx.cHatSpawnVeloSphereRadius,
            v => runtimeSettings.handVfx.cHatSpawnVeloSphereRadius = v
        );
        CreateFloatField(
            group,
            "OHat Size (×s)",
            () => runtimeSettings.handVfx.oHatSize,
            v => runtimeSettings.handVfx.oHatSize = v
        );
        CreateFloatField(
            group,
            "OHat Noise Amp (×s)",
            () => runtimeSettings.handVfx.oHatNoiseAmp,
            v => runtimeSettings.handVfx.oHatNoiseAmp = v
        );
        CreateFloatField(
            group,
            "OHat Noise Freq (×1/s)",
            () => runtimeSettings.handVfx.oHatNoiseFreq,
            v => runtimeSettings.handVfx.oHatNoiseFreq = v
        );
        CreateFloatField(
            group,
            "OHat Noise Y Scroll (×s)",
            () => runtimeSettings.handVfx.oHatNoiseYScroll,
            v => runtimeSettings.handVfx.oHatNoiseYScroll = v
        );
    }

    private void CreateHandVfxSnareGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hand VFX - Snare", parentContainer);

        CreateVector2Field(
            group,
            "Snare Size Range (×s)",
            () => runtimeSettings.handVfx.snareSizeRange,
            v => runtimeSettings.handVfx.snareSizeRange = v
        );
        CreateVector2Field(
            group,
            "Snare Radius Rand Range (×s)",
            () => runtimeSettings.handVfx.snareRadiusRandRange,
            v => runtimeSettings.handVfx.snareRadiusRandRange = v
        );
        CreateFloatField(
            group,
            "Snare Spawn Velo Sphere Radius (×s)",
            () => runtimeSettings.handVfx.snareSpawnVeloSphereRadius,
            v => runtimeSettings.handVfx.snareSpawnVeloSphereRadius = v
        );
    }

    private void CreatePostProcessingGroup(ScrollView parentContainer)
    {
        var bloomGroup = CreateGroup("Bloom", parentContainer);

        CreateFloatField(
            bloomGroup,
            "Bloom Threshold",
            () => runtimeSettings.bloomThreshold,
            v => runtimeSettings.bloomThreshold = v
        );
        CreateSliderField(
            bloomGroup,
            "Bloom Intensity",
            () => runtimeSettings.bloomIntensity,
            v => runtimeSettings.bloomIntensity = v,
            0f,
            3f
        );
        CreateSliderField(
            bloomGroup,
            "Bloom Scatter",
            () => runtimeSettings.bloomScatter,
            v => runtimeSettings.bloomScatter = v,
            0f,
            1f
        );

        var lensFlareGroup = CreateGroup("Screen Space Lens Flare", parentContainer);

        CreateSliderField(
            lensFlareGroup,
            "Intensity",
            () => runtimeSettings.lensFlareIntensity,
            v => runtimeSettings.lensFlareIntensity = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Regular Multiplier (Flares)",
            () => runtimeSettings.lensFlareRegularMultiplier,
            v => runtimeSettings.lensFlareRegularMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Reversed Multiplier (Flares)",
            () => runtimeSettings.lensFlareReversedMultiplier,
            v => runtimeSettings.lensFlareReversedMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Multiplier (Streaks)",
            () => runtimeSettings.lensFlareStreaksMultiplier,
            v => runtimeSettings.lensFlareStreaksMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Length (Streaks)",
            () => runtimeSettings.lensFlareStreaksLength,
            v => runtimeSettings.lensFlareStreaksLength = v,
            0f,
            1f
        );
        CreateSliderField(
            lensFlareGroup,
            "Orientation (Streaks)",
            () => runtimeSettings.lensFlareStreaksOrientation,
            v => runtimeSettings.lensFlareStreaksOrientation = v,
            -180f,
            180f
        );
        CreateSliderField(
            lensFlareGroup,
            "Threshold (Streaks)",
            () => runtimeSettings.lensFlareStreaksThreshold,
            v => runtimeSettings.lensFlareStreaksThreshold = v,
            0f,
            1f
        );
        CreateSliderField(
            lensFlareGroup,
            "Chromatic Aberration Intensity",
            () => runtimeSettings.lensFlareChromaticIntensity,
            v => runtimeSettings.lensFlareChromaticIntensity = v,
            0f,
            1f
        );

        var lensDistortionGroup = CreateGroup("Lens Distortion", parentContainer);

        CreateSliderField(
            lensDistortionGroup,
            "Intensity",
            () => runtimeSettings.lensDistortionIntensity,
            v => runtimeSettings.lensDistortionIntensity = v,
            -1f,
            1f
        );
        CreateSliderField(
            lensDistortionGroup,
            "X Multiplier",
            () => runtimeSettings.lensDistortionXMultiplier,
            v => runtimeSettings.lensDistortionXMultiplier = v,
            0f,
            2f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Y Multiplier",
            () => runtimeSettings.lensDistortionYMultiplier,
            v => runtimeSettings.lensDistortionYMultiplier = v,
            0f,
            2f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Scale",
            () => runtimeSettings.lensDistortionScale,
            v => runtimeSettings.lensDistortionScale = v,
            0.01f,
            3f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Center X",
            () => runtimeSettings.lensDistortionCenterX,
            v => runtimeSettings.lensDistortionCenterX = v,
            0f,
            1f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Center Y",
            () => runtimeSettings.lensDistortionCenterY,
            v => runtimeSettings.lensDistortionCenterY = v,
            0f,
            1f
        );

        var colorAdjustmentsGroup = CreateGroup("Color Adjustments", parentContainer);

        CreateFloatField(
            colorAdjustmentsGroup,
            "Post Exposure",
            () => runtimeSettings.colorAdjustmentsPostExposure,
            v => runtimeSettings.colorAdjustmentsPostExposure = v
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Contrast",
            () => runtimeSettings.colorAdjustmentsContrast,
            v => runtimeSettings.colorAdjustmentsContrast = v,
            -100f,
            100f
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Hue Shift",
            () => runtimeSettings.colorAdjustmentsHueShift,
            v => runtimeSettings.colorAdjustmentsHueShift = v,
            -180f,
            180f
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Saturation",
            () => runtimeSettings.colorAdjustmentsSaturation,
            v => runtimeSettings.colorAdjustmentsSaturation = v,
            -100f,
            100f
        );

        var whiteBalanceGroup = CreateGroup("White Balance", parentContainer);

        CreateSliderField(
            whiteBalanceGroup,
            "Temperature",
            () => runtimeSettings.whiteBalanceTemperature,
            v => runtimeSettings.whiteBalanceTemperature = v,
            -100f,
            100f
        );
        CreateSliderField(
            whiteBalanceGroup,
            "Tint",
            () => runtimeSettings.whiteBalanceTint,
            v => runtimeSettings.whiteBalanceTint = v,
            -100f,
            100f
        );
    }

    private void CreateStyleGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Style", parentContainer);

        CreateToggleField(
            group,
            "Custom Colors",
            () => runtimeSettings.customColors,
            v => runtimeSettings.customColors = v,
            tooltip: "Assign each new player a color from the custom palette (set in the SceneController inspector) instead of the default gradient."
        );
        CreateToggleField(
            group,
            "Draw Skeleton",
            () => runtimeSettings.drawSkeleton,
            v => runtimeSettings.drawSkeleton = v,
            tooltip: "Draw line-renderer bones between tracked Kinect joints for each player."
        );
        CreateToggleField(
            group,
            "Use Tracking State Colors",
            () => runtimeSettings.useTrackingStateColors,
            v => runtimeSettings.useTrackingStateColors = v,
            tooltip: "Color skeleton bones by Kinect joint tracking state (tracked / inferred / not tracked) instead of the player's color. Only applies when Draw Skeleton is on."
        );
    }

    private void CreateDebuggingGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Debugging", parentContainer);

        CreateToggleField(
            group,
            "Dummy Only Mode",
            () => runtimeSettings.dummyOnlyMode,
            v => runtimeSettings.dummyOnlyMode = v,
            tooltip: "Skip the Kinect entirely and drive the scene with dummy players only. For development without a sensor."
        );
        CreateToggleField(
            group,
            "Show Sphere Mesh On Hand Collision",
            () => runtimeSettings.showSphereMeshOnHandCollision,
            v => runtimeSettings.showSphereMeshOnHandCollision = v,
            tooltip: "Temporarily show the physics sphere mesh whenever a hand's scaling ray hits the ball, to visualize the grow/shrink hit test."
        );
        CreateToggleField(
            group,
            "Always Show Sphere Mesh",
            () => runtimeSettings.alwaysShowSphereMesh,
            v => runtimeSettings.alwaysShowSphereMesh = v,
            tooltip: "When enabled, the sphere mesh is always visible regardless of hand collision state."
        );
        CreateToggleField(
            group,
            "Show Metaball Mesh",
            () => runtimeSettings.showMetaballMesh,
            v => runtimeSettings.showMetaballMesh = v,
            tooltip: "When enabled, the metaball mesh renderer is visible for debugging."
        );
        CreateToggleField(
            group,
            "Show Point Cloud",
            () => runtimeSettings.showPointCloud,
            v => runtimeSettings.showPointCloud = v,
            tooltip: "When enabled, the Kinect depth point cloud (body occlusion geometry) is rendered visibly."
        );
        CreateToggleField(
            group,
            "Show Metaball Bounds",
            () => runtimeSettings.showMetaballBounds,
            v => runtimeSettings.showMetaballBounds = v,
            tooltip: "When enabled, the metaball volume's bounding box is drawn as a wireframe."
        );
        CreateToggleField(
            group,
            "Show Attraction Radius",
            () => runtimeSettings.showAttractionRadius,
            v => runtimeSettings.showAttractionRadius = v,
            tooltip: "Show a sprite around each ball indicating its gravity attraction radius."
        );
        CreateToggleField(
            group,
            "Show Hand Trail Distorters",
            () => runtimeSettings.showHandTrailDistorters,
            v => runtimeSettings.showHandTrailDistorters = v,
            tooltip: "Render the TD1/TD2 trail distorter debug spheres that orbit each hand and shape the hand particles."
        );
        CreateToggleField(
            group,
            "Show Secondary Attractor",
            () => runtimeSettings.showSecondaryAttractor,
            v => runtimeSettings.showSecondaryAttractor = v,
            tooltip: "Render the secondary attractor debug sphere on each hand that the hand particles conform to."
        );
    }

    private VisualElement CreateGroup(string title, ScrollView parentContainer)
    {
        var group = new VisualElement();
        group.AddToClassList("settings-group");

        var header = new Label(title);
        header.AddToClassList("group-header");
        group.Add(header);

        parentContainer.Add(group);
        settingGroups.Add(group);

        return group;
    }

    // ---- Tooltips ----

    private void AttachTooltip(VisualElement target, string tooltip)
    {
        if (string.IsNullOrEmpty(tooltip))
            return;

        target.RegisterCallback<PointerEnterEvent>(evt => ShowTooltip(tooltip, evt.position));
        target.RegisterCallback<PointerMoveEvent>(evt => MoveTooltip(evt.position));
        target.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
    }

    private void ShowTooltip(string text, Vector2 panelPosition)
    {
        if (tooltipElement == null)
            return;
        tooltipElement.text = text;
        tooltipElement.style.display = DisplayStyle.Flex;
        tooltipElement.BringToFront();
        MoveTooltip(panelPosition);
    }

    private void MoveTooltip(Vector2 panelPosition)
    {
        if (tooltipElement == null || tooltipElement.style.display == DisplayStyle.None)
            return;

        const float offset = 16f;
        var root = uiDocument.rootVisualElement;
        float x = panelPosition.x + offset;
        float y = panelPosition.y + offset;

        // Keep the popup inside the panel once its size is known.
        float w = tooltipElement.resolvedStyle.width;
        float h = tooltipElement.resolvedStyle.height;
        if (!float.IsNaN(w) && w > 0 && x + w > root.resolvedStyle.width)
            x = Mathf.Max(0f, panelPosition.x - w - offset);
        if (!float.IsNaN(h) && h > 0 && y + h > root.resolvedStyle.height)
            y = Mathf.Max(0f, panelPosition.y - h - offset);

        tooltipElement.style.left = x;
        tooltipElement.style.top = y;
    }

    private void HideTooltip()
    {
        if (tooltipElement != null)
            tooltipElement.style.display = DisplayStyle.None;
    }

    private void CreateFloatField(
        VisualElement parent,
        string label,
        Func<float> getter,
        Action<float> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var field = new FloatField();
        field.name = label;
        field.AddToClassList("setting-input");
        field.value = getter();
        field.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            NotifySettingsChanged();
        });

        row.Add(labelElement);
        row.Add(field);
        parent.Add(row);

        settingElements[label] = field;
    }

    private void CreateIntField(
        VisualElement parent,
        string label,
        Func<int> getter,
        Action<int> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var field = new IntegerField();
        field.name = label;
        field.AddToClassList("setting-input");
        field.value = getter();
        field.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            NotifySettingsChanged();
        });

        row.Add(labelElement);
        row.Add(field);
        parent.Add(row);

        settingElements[label] = field;
    }

    private void CreateVector2Field(
        VisualElement parent,
        string label,
        Func<Vector2> getter,
        Action<Vector2> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var field = new Vector2Field();
        field.name = label;
        field.AddToClassList("setting-input");
        field.value = getter();
        field.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            NotifySettingsChanged();
        });

        row.Add(labelElement);
        row.Add(field);
        parent.Add(row);

        settingElements[label] = field;
    }

    private void CreateVector3Field(
        VisualElement parent,
        string label,
        Func<Vector3> getter,
        Action<Vector3> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var field = new Vector3Field();
        field.name = label;
        field.AddToClassList("setting-input");
        field.value = getter();
        field.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            NotifySettingsChanged();
        });

        row.Add(labelElement);
        row.Add(field);
        parent.Add(row);

        settingElements[label] = field;
    }

    private void CreateSliderField(
        VisualElement parent,
        string label,
        Func<float> getter,
        Action<float> setter,
        float min,
        float max,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var inputContainer = new VisualElement();
        inputContainer.style.flexDirection = FlexDirection.Row;
        inputContainer.style.alignItems = Align.Center;
        inputContainer.AddToClassList("setting-input");

        var slider = new Slider(min, max);
        slider.style.flexGrow = 1;
        slider.value = getter();

        var valueLabel = new Label($"{getter():F2}");
        valueLabel.style.minWidth = 50;
        valueLabel.style.unityTextAlign = TextAnchor.MiddleRight;
        valueLabel.style.color = Color.gray;
        valueLabel.style.marginLeft = 5;

        slider.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            valueLabel.text = $"{evt.newValue:F2}";
            NotifySettingsChanged();
        });

        inputContainer.Add(slider);
        inputContainer.Add(valueLabel);

        row.Add(labelElement);
        row.Add(inputContainer);
        parent.Add(row);

        settingElements[label] = slider;
        settingElements[label + "_ValueLabel"] = valueLabel;
    }

    private void CreateToggleField(
        VisualElement parent,
        string label,
        Func<bool> getter,
        Action<bool> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var toggle = new Toggle();
        toggle.AddToClassList("toggle");
        toggle.value = getter();
        toggle.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            NotifySettingsChanged();
        });

        row.Add(labelElement);
        row.Add(toggle);
        parent.Add(row);

        settingElements[label] = toggle;
    }

    private void CreateCurveField(
        VisualElement parent,
        string label,
        Func<AnimationCurve> getter,
        Action<AnimationCurve> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        // Use a regular VisualElement with a CPU-rendered Texture2D instead of
        // IMGUIContainer + GL calls. GL.LoadPixelMatrix() always uses screen coordinates
        // which don't match the local coordinate space inside UI Toolkit containers.
        var thumbnail = new VisualElement();
        thumbnail.AddToClassList("curve-thumbnail");

        Texture2D curveTex = null;
        bool hasRendered = false;

        // Schedule periodic texture update — renders once initially, then only
        // re-renders while the curve editor is open (the only time curves change).
        // Profile loads recreate the entire UI so thumbnails get a fresh initial render.
        thumbnail
            .schedule.Execute(() =>
            {
                if (hasRendered && !RuntimeCurveEditorWindow.IsVisible)
                    return;

                var curve = getter();
                if (curve == null || curve.length == 0)
                    return;

                int width = Mathf.Max(Mathf.RoundToInt(thumbnail.resolvedStyle.width), 4);
                int height = Mathf.Max(Mathf.RoundToInt(thumbnail.resolvedStyle.height), 4);
                if (width <= 4 || height <= 4)
                    return;

                if (curveTex == null || curveTex.width != width || curveTex.height != height)
                {
                    if (curveTex != null)
                    {
                        curveTextures.Remove(curveTex);
                        Destroy(curveTex);
                    }
                    curveTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    curveTex.filterMode = FilterMode.Bilinear;
                    curveTex.wrapMode = TextureWrapMode.Clamp;
                    curveTextures.Add(curveTex);
                }

                RenderCurveToTexture(curve, curveTex, new Color32(0, 204, 0, 230));
                thumbnail.style.backgroundImage = curveTex;
                hasRendered = true;
            })
            .Every(200);

        // Click to open the curve editor popup
        thumbnail.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (RuntimeCurveEditorWindow.IsVisible)
                return;

            var curve = getter();
            if (curve == null)
                return;

            RuntimeCurveEditorWindow.Show(
                curve,
                changedCurve =>
                {
                    setter(changedCurve);
                    NotifySettingsChanged();
                }
            );
        });

        row.Add(labelElement);
        row.Add(thumbnail);
        parent.Add(row);

        settingElements[label] = thumbnail;
    }

    private static void RenderCurveToTexture(
        AnimationCurve curve,
        Texture2D tex,
        Color32 curveColor
    )
    {
        int width = tex.width;
        int height = tex.height;
        Color32 clear = new Color32(0, 0, 0, 0);

        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = clear;

        // Compute curve value bounds by sampling
        float minTime = curve[0].time;
        float maxTime = curve[curve.length - 1].time;
        float timeRange = maxTime - minTime;
        if (timeRange < 0.001f)
        {
            timeRange = 1f;
            minTime -= 0.5f;
            maxTime += 0.5f;
        }

        float minVal = float.MaxValue,
            maxVal = float.MinValue;
        int sampleCount = width * 2;
        for (int i = 0; i <= sampleCount; i++)
        {
            float v = curve.Evaluate(minTime + timeRange * i / sampleCount);
            if (v < minVal)
                minVal = v;
            if (v > maxVal)
                maxVal = v;
        }
        float valRange = maxVal - minVal;
        if (valRange < 0.001f)
        {
            valRange = 1f;
            minVal -= 0.5f;
        }

        // Add padding
        float pad = valRange * 0.1f;
        minVal -= pad;
        valRange = (maxVal + pad) - minVal;
        float padTime = timeRange * 0.05f;
        minTime -= padTime;
        timeRange += padTime * 2f;

        // Draw curve line connecting adjacent samples
        int prevY = -1;
        for (int x = 0; x < width; x++)
        {
            float t = minTime + timeRange * x / (width - 1);
            float v = curve.Evaluate(t);
            int y = Mathf.Clamp(
                Mathf.RoundToInt((v - minVal) / valRange * (height - 1)),
                0,
                height - 1
            );

            if (prevY >= 0 && Mathf.Abs(y - prevY) > 1)
            {
                // Fill vertical gap between consecutive samples
                int lo = Mathf.Min(prevY, y);
                int hi = Mathf.Max(prevY, y);
                for (int fillY = lo; fillY <= hi; fillY++)
                    pixels[fillY * width + x] = curveColor;
            }
            else
            {
                pixels[y * width + x] = curveColor;
            }

            prevY = y;
        }

        tex.SetPixels32(pixels);
        tex.Apply();
    }

    private void CreateFloatArrayField(
        VisualElement parent,
        string label,
        Func<float[]> getter,
        Action<float[]> setter,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var arrayContainer = new VisualElement();
        arrayContainer.AddToClassList("array-container");

        // Create header container for collapse button and controls
        var headerContainer = new VisualElement();
        headerContainer.AddToClassList("array-header");

        var collapseButton = new Button();
        collapseButton.text = "⇓"; // Right arrow for collapsed state
        collapseButton.AddToClassList("array-collapse-button");

        var countLabel = new Label();
        countLabel.AddToClassList("array-count-label");

        var addButton = new Button(() =>
            AddArrayElement(arrayContainer, getter, setter, collapseButton, countLabel)
        );
        addButton.text = "Add Element";
        addButton.AddToClassList("array-button");
        addButton.AddToClassList("hidden"); // Start hidden since we start collapsed

        // Create content container that will be hidden/shown
        var contentContainer = new VisualElement();
        contentContainer.AddToClassList("array-content");
        contentContainer.AddToClassList("collapsed"); // Start collapsed

        headerContainer.Add(collapseButton);
        headerContainer.Add(countLabel);
        headerContainer.Add(addButton);

        arrayContainer.Add(headerContainer);
        arrayContainer.Add(contentContainer);

        // Setup collapse/expand functionality
        bool isCollapsed = true;
        collapseButton.clicked += () =>
        {
            isCollapsed = !isCollapsed;
            if (isCollapsed)
            {
                collapseButton.text = "⇓";
                contentContainer.AddToClassList("collapsed");
                addButton.AddToClassList("hidden");
            }
            else
            {
                collapseButton.text = "⇑";
                contentContainer.RemoveFromClassList("collapsed");
                addButton.RemoveFromClassList("hidden");
            }
        };

        RefreshFloatArray(arrayContainer, getter(), setter, collapseButton, countLabel);

        var container = new VisualElement();
        container.AddToClassList("array-row-subcontainer");
        container.Add(labelElement);
        container.Add(arrayContainer);
        row.Add(container);
        parent.Add(row);

        settingElements[label] = arrayContainer;
    }

    private void AddArrayElement(
        VisualElement container,
        Func<float[]> getter,
        Action<float[]> setter,
        Button collapseButton,
        Label countLabel
    )
    {
        var currentArray = getter();
        var newArray = new float[currentArray.Length + 1];
        Array.Copy(currentArray, newArray, currentArray.Length);
        newArray[newArray.Length - 1] = 1f;
        setter(newArray);
        RefreshFloatArray(container, newArray, setter, collapseButton, countLabel);
        NotifySettingsChanged();
    }

    private void RefreshFloatArray(
        VisualElement container,
        float[] array,
        Action<float[]> setter,
        Button collapseButton,
        Label countLabel
    )
    {
        // Update count label
        countLabel.text = $"({array.Length} items)";

        // Find the content container (second child after header)
        var contentContainer = container.Children().ElementAt(1);
        contentContainer.Clear();

        for (int i = 0; i < array.Length; i++)
        {
            int index = i; // Capture for closure
            var elementRow = new VisualElement();
            elementRow.AddToClassList("array-element");

            var field = new FloatField();
            field.AddToClassList("array-element-input");
            field.value = array[index];
            field.RegisterValueChangedCallback(evt =>
            {
                array[index] = evt.newValue;
                setter(array);
                NotifySettingsChanged();
            });

            var removeButton = new Button(() =>
            {
                var newArray = new float[array.Length - 1];
                Array.Copy(array, 0, newArray, 0, index);
                Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
                setter(newArray);
                RefreshFloatArray(container, newArray, setter, collapseButton, countLabel);
                NotifySettingsChanged();
            });
            removeButton.text = "-";
            removeButton.AddToClassList("array-button");

            elementRow.Add(field);
            elementRow.Add(removeButton);
            contentContainer.Add(elementRow);
        }
    }

    public void ToggleMenu()
    {
        if (settingsPanel.ClassListContains("hidden"))
        {
            ShowMenu();
        }
        else
        {
            CloseMenu();
        }
    }

    private void ShowMenu()
    {
        settingsPanel.RemoveFromClassList("hidden");
        RefreshUI();

        // Show cursor when menu is open (only outside Unity editor)
        if (!Application.isEditor)
        {
            UnityEngine.Cursor.visible = true;
        }
    }

    private void CloseMenu()
    {
        settingsPanel.AddToClassList("hidden");

        // Hide cursor when menu is closed (only outside Unity editor)
        if (!Application.isEditor)
        {
            UnityEngine.Cursor.visible = false;
        }
    }

    private void RefreshUI()
    {
        if (runtimeSettings == null)
            return;

        // Recreate the entire UI to ensure all values are current
        CreateSettingsUI();
    }

    private void RefreshSceneProfiles()
    {
        if (sceneProfileDropdown == null)
            return;

        // Ensure we're using scene-specific keys
        EnsureSceneSpecificKeys();

        var profileFiles = Directory
            .GetFiles(sceneProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        sceneProfileDropdown.choices = profileFiles;

        // Skip loading profile if refresh is suppressed (during save operations)
        if (isRefreshingSuppressed)
            return;

        // Working set restored: just point the dropdown at the profile it came from.
        if (restoredFromWorkingSet)
        {
            string name = Path.GetFileNameWithoutExtension(currentSceneProfilePath ?? "");
            if (!string.IsNullOrEmpty(name) && profileFiles.Contains(name))
                sceneProfileDropdown.SetValueWithoutNotify(name);
            return;
        }

        // Try to restore last used scene profile for this specific scene
        string lastUsedProfile = PlayerPrefs.GetString(lastUsedSceneProfileKey, "");

        if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
        {
            sceneProfileDropdown.SetValueWithoutNotify(lastUsedProfile);
            LoadProfile(
                Path.Combine(sceneProfilesDirectory, lastUsedProfile + ".json"),
                ProfileType.Scene
            );
        }
        else if (profileFiles.Count > 0)
        {
            // If no scene-specific profile exists, use the first available but don't save it as preference yet
            sceneProfileDropdown.SetValueWithoutNotify(profileFiles[0]);
            LoadProfile(
                Path.Combine(sceneProfilesDirectory, profileFiles[0] + ".json"),
                ProfileType.Scene
            );
        }
    }

    private void RefreshPostProcessingProfiles()
    {
        if (postProcessingProfileDropdown == null)
            return;

        // Ensure we're using scene-specific keys
        EnsureSceneSpecificKeys();

        var profileFiles = Directory
            .GetFiles(postProcessingProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        postProcessingProfileDropdown.choices = profileFiles;

        if (isRefreshingSuppressed)
            return;

        // Working set restored: just point the dropdown at the profile it came from.
        if (restoredFromWorkingSet)
        {
            string name = Path.GetFileNameWithoutExtension(currentPostProcessingProfilePath ?? "");
            if (!string.IsNullOrEmpty(name) && profileFiles.Contains(name))
                postProcessingProfileDropdown.SetValueWithoutNotify(name);
            return;
        }

        // Try to restore last used post-processing profile for this specific scene
        string lastUsedProfile = PlayerPrefs.GetString(lastUsedPostProcessingProfileKey, "");
        if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
        {
            postProcessingProfileDropdown.SetValueWithoutNotify(lastUsedProfile);
            LoadProfile(
                Path.Combine(postProcessingProfilesDirectory, lastUsedProfile + ".json"),
                ProfileType.PostProcessing
            );
        }
        else if (profileFiles.Count > 0)
        {
            // If no scene-specific profile exists, use the first available but don't save it as preference yet
            postProcessingProfileDropdown.SetValueWithoutNotify(profileFiles[0]);
            LoadProfile(
                Path.Combine(postProcessingProfilesDirectory, profileFiles[0] + ".json"),
                ProfileType.PostProcessing
            );
        }
    }

    private void RefreshProfileDropdowns()
    {
        RefreshSceneProfiles();
        RefreshPostProcessingProfiles();
    }

    private void LoadSelectedProfile(string tabType)
    {
        if (tabType == "scene")
        {
            if (sceneProfileDropdown == null || string.IsNullOrEmpty(sceneProfileDropdown.value))
                return;
            var profilePath = Path.Combine(
                sceneProfilesDirectory,
                sceneProfileDropdown.value + ".json"
            );
            LoadProfile(profilePath, ProfileType.Scene);
        }
        else if (tabType == "postprocessing")
        {
            if (
                postProcessingProfileDropdown == null
                || string.IsNullOrEmpty(postProcessingProfileDropdown.value)
            )
                return;
            var profilePath = Path.Combine(
                postProcessingProfilesDirectory,
                postProcessingProfileDropdown.value + ".json"
            );
            LoadProfile(profilePath, ProfileType.PostProcessing);
        }
    }

    private void LoadProfile(string path, ProfileType profileType)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);
            var loadedSettings = JsonUtility.FromJson<RuntimeSceneSettings>(json);

            // Merge loaded settings based on profile type
            if (profileType == ProfileType.Scene)
            {
                // Legacy (version 0) scene files hold effective values tuned at their own
                // bodyScale - convert to base-at-1x in memory (never written back here).
                // PP profiles are version 0 too and must NOT be touched.
                if (loadedSettings.settingsVersion < RuntimeSceneSettings.CurrentSettingsVersion)
                {
                    BodyScaling.ConvertLegacyProfileInPlace(loadedSettings, json);
                    Debug.Log(
                        $"[InGameSettingsMenu] '{Path.GetFileName(path)}' is a legacy (v0) scene profile - "
                            + "converted to base values in memory. Save it (or run EnergyBall/Migrate Scene Profiles To Base) to persist."
                    );
                }

                // Load only scene settings, keep current post-processing settings
                MergeSceneSettings(loadedSettings);
                currentSceneProfilePath = path;
                sceneBaselineJson = CanonicalSceneJson(runtimeSettings);

                // Save as last used scene profile
                string profileName = Path.GetFileNameWithoutExtension(path);
                PlayerPrefs.SetString(lastUsedSceneProfileKey, profileName);
                PlayerPrefs.Save();
            }
            else if (profileType == ProfileType.PostProcessing)
            {
                // Load only post-processing settings, keep current scene settings
                MergePostProcessingSettings(loadedSettings);
                currentPostProcessingProfilePath = path;
                postProcessingBaselineJson = CanonicalPostProcessingJson(runtimeSettings);

                // Save as last used post-processing profile
                string profileName = Path.GetFileNameWithoutExtension(path);
                PlayerPrefs.SetString(lastUsedPostProcessingProfileKey, profileName);
                PlayerPrefs.Save();

                // Update Volume Profile with post-processing settings (during play mode)
                if (Application.isPlaying && Controller?.volumeController != null)
                {
                    Controller.volumeController.ApplyCurrentSettings(runtimeSettings);
                }
            }

            RefreshUI();
            NotifySettingsChanged();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load profile: {e.Message}");
        }
    }

    private void MergeSceneSettings(RuntimeSceneSettings loadedSettings)
    {
        // Copy only non-post-processing settings from loaded profile
        // Keep the current post-processing settings intact
        runtimeSettings.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;

        // Gravity and Force settings
        runtimeSettings.g = loadedSettings.g;
        runtimeSettings.maxTowardsForce = loadedSettings.maxTowardsForce;
        runtimeSettings.maxAwayFromForce = loadedSettings.maxAwayFromForce;
        runtimeSettings.gravityForceDamper = loadedSettings.gravityForceDamper;
        runtimeSettings.stopGravityDistance = loadedSettings.stopGravityDistance;
        runtimeSettings.stopMovingDistance = loadedSettings.stopMovingDistance;
        runtimeSettings.stopVelocity = loadedSettings.stopVelocity;
        runtimeSettings.attractionRadiusMultiplier = loadedSettings.attractionRadiusMultiplier;

        // Curves (included in profiles)
        if (loadedSettings.forceToMiddle != null && loadedSettings.forceToMiddle.length > 0)
            runtimeSettings.forceToMiddle = new AnimationCurve(loadedSettings.forceToMiddle.keys);

        // Hand interaction settings
        runtimeSettings.singleHandOpenForceDamper = loadedSettings.singleHandOpenForceDamper;
        runtimeSettings.pushForce = loadedSettings.pushForce;
        runtimeSettings.torsoMaxForwardOffset = loadedSettings.torsoMaxForwardOffset;
        runtimeSettings.torsoOffsetFalloffDistance = loadedSettings.torsoOffsetFalloffDistance;
        runtimeSettings.minDrag = loadedSettings.minDrag;
        runtimeSettings.maxDrag = loadedSettings.maxDrag;
        runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
        runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
        runtimeSettings.outOfBoundsResetDelay = loadedSettings.outOfBoundsResetDelay;
        if (
            loadedSettings.alignmentVectorStrength != null
            && loadedSettings.alignmentVectorStrength.length > 0
        )
            runtimeSettings.alignmentVectorStrength = new AnimationCurve(
                loadedSettings.alignmentVectorStrength.keys
            );
        runtimeSettings.alignmentVectorStrengthScaler =
            loadedSettings.alignmentVectorStrengthScaler;
        runtimeSettings.handPushScaler = loadedSettings.handPushScaler;
        runtimeSettings.prayToActivate = loadedSettings.prayToActivate;
        runtimeSettings.prayToActivateDistance = loadedSettings.prayToActivateDistance;

        // Pulsation settings
        runtimeSettings.pulseAmount = loadedSettings.pulseAmount;
        runtimeSettings.pulseSpeed = loadedSettings.pulseSpeed;
        runtimeSettings.graphLimit = loadedSettings.graphLimit;
        runtimeSettings.pulseFreqs = loadedSettings.pulseFreqs;

        // Size and scaling settings
        runtimeSettings.singleHandScaling = loadedSettings.singleHandScaling;
        runtimeSettings.minimumUnscaledSize = loadedSettings.minimumUnscaledSize;
        runtimeSettings.maximumUnscaledSize = loadedSettings.maximumUnscaledSize;
        runtimeSettings.minHandDisplacementPerFrame = loadedSettings.minHandDisplacementPerFrame;
        runtimeSettings.maxHandVelocity = loadedSettings.maxHandVelocity;
        if (loadedSettings.distanceDamper != null && loadedSettings.distanceDamper.length > 0)
            runtimeSettings.distanceDamper = new AnimationCurve(loadedSettings.distanceDamper.keys);
        runtimeSettings.pulseScaleDamper = loadedSettings.pulseScaleDamper;
        runtimeSettings.mergeSizeScalerDamper = loadedSettings.mergeSizeScalerDamper;
        runtimeSettings.maxDistanceBetweenHands = loadedSettings.maxDistanceBetweenHands;
        runtimeSettings.baseZDepth = loadedSettings.baseZDepth;
        runtimeSettings.gridScale = loadedSettings.gridScale;
        runtimeSettings.defaultUnscaledSize = loadedSettings.defaultUnscaledSize;
        runtimeSettings.bodyScale = loadedSettings.bodyScale;
        runtimeSettings.maxDistanceFromCamera = loadedSettings.maxDistanceFromCamera;
        runtimeSettings.sphereResetJitter = loadedSettings.sphereResetJitter;

        // Hand VFX (nested group, copied as one object; old files without the key get C# defaults)
        runtimeSettings.handVfx =
            loadedSettings.handVfx != null
                ? loadedSettings.handVfx.DeepCopy()
                : new HandVfxSettings();

        // Animation
        runtimeSettings.particleInitializationDelay = loadedSettings.particleInitializationDelay;
        runtimeSettings.initializationResetDelay = loadedSettings.initializationResetDelay;
        runtimeSettings.singleHandOpenThreshold = loadedSettings.singleHandOpenThreshold;
        runtimeSettings.singleHandForceLerpDuration = loadedSettings.singleHandForceLerpDuration;
        runtimeSettings.initializationSpeed = loadedSettings.initializationSpeed;
        runtimeSettings.metaballRadiusAnimationDuration =
            loadedSettings.metaballRadiusAnimationDuration;
        runtimeSettings.metaballRadiusAnimationStartSize =
            loadedSettings.metaballRadiusAnimationStartSize;
        runtimeSettings.bodySpawnSize = loadedSettings.bodySpawnSize;
        if (
            loadedSettings.metaballRadiusAnimationCurve != null
            && loadedSettings.metaballRadiusAnimationCurve.length > 0
        )
            runtimeSettings.metaballRadiusAnimationCurve = new AnimationCurve(
                loadedSettings.metaballRadiusAnimationCurve.keys
            );

        // Style settings
        runtimeSettings.customColors = loadedSettings.customColors;
        runtimeSettings.drawSkeleton = loadedSettings.drawSkeleton;
        runtimeSettings.useTrackingStateColors = loadedSettings.useTrackingStateColors;

        // Debug settings
        runtimeSettings.dummyOnlyMode = loadedSettings.dummyOnlyMode;
        runtimeSettings.showSphereMeshOnHandCollision =
            loadedSettings.showSphereMeshOnHandCollision;
        runtimeSettings.alwaysShowSphereMesh = loadedSettings.alwaysShowSphereMesh;
        runtimeSettings.showMetaballMesh = loadedSettings.showMetaballMesh;
        runtimeSettings.showPointCloud = loadedSettings.showPointCloud;
        runtimeSettings.showMetaballBounds = loadedSettings.showMetaballBounds;
        runtimeSettings.showAttractionRadius = loadedSettings.showAttractionRadius;
        runtimeSettings.showHandTrailDistorters = loadedSettings.showHandTrailDistorters;
        runtimeSettings.showSecondaryAttractor = loadedSettings.showSecondaryAttractor;
    }

    private void MergePostProcessingSettings(RuntimeSceneSettings loadedSettings)
    {
        // Copy only post-processing settings from loaded profile
        // Keep the current scene settings intact

        // Bloom settings
        runtimeSettings.bloomThreshold = loadedSettings.bloomThreshold;
        runtimeSettings.bloomIntensity = loadedSettings.bloomIntensity;
        runtimeSettings.bloomScatter = loadedSettings.bloomScatter;

        // Lens Flare settings
        runtimeSettings.lensFlareIntensity = loadedSettings.lensFlareIntensity;
        runtimeSettings.lensFlareRegularMultiplier = loadedSettings.lensFlareRegularMultiplier;
        runtimeSettings.lensFlareReversedMultiplier = loadedSettings.lensFlareReversedMultiplier;
        runtimeSettings.lensFlareStreaksMultiplier = loadedSettings.lensFlareStreaksMultiplier;
        runtimeSettings.lensFlareStreaksLength = loadedSettings.lensFlareStreaksLength;
        runtimeSettings.lensFlareStreaksOrientation = loadedSettings.lensFlareStreaksOrientation;
        runtimeSettings.lensFlareStreaksThreshold = loadedSettings.lensFlareStreaksThreshold;
        runtimeSettings.lensFlareChromaticIntensity = loadedSettings.lensFlareChromaticIntensity;

        // Lens Distortion settings
        runtimeSettings.lensDistortionIntensity = loadedSettings.lensDistortionIntensity;
        runtimeSettings.lensDistortionXMultiplier = loadedSettings.lensDistortionXMultiplier;
        runtimeSettings.lensDistortionYMultiplier = loadedSettings.lensDistortionYMultiplier;
        runtimeSettings.lensDistortionScale = loadedSettings.lensDistortionScale;
        runtimeSettings.lensDistortionCenterX = loadedSettings.lensDistortionCenterX;
        runtimeSettings.lensDistortionCenterY = loadedSettings.lensDistortionCenterY;

        // Color Adjustments settings
        runtimeSettings.colorAdjustmentsPostExposure = loadedSettings.colorAdjustmentsPostExposure;
        runtimeSettings.colorAdjustmentsContrast = loadedSettings.colorAdjustmentsContrast;
        runtimeSettings.colorAdjustmentsHueShift = loadedSettings.colorAdjustmentsHueShift;
        runtimeSettings.colorAdjustmentsSaturation = loadedSettings.colorAdjustmentsSaturation;

        // White Balance settings
        runtimeSettings.whiteBalanceTemperature = loadedSettings.whiteBalanceTemperature;
        runtimeSettings.whiteBalanceTint = loadedSettings.whiteBalanceTint;
    }

    private void CopySceneSettings(RuntimeSceneSettings source, RuntimeSceneSettings destination)
    {
        // Copy only non-post-processing settings to destination.
        // Saved scene profiles are always base-at-1x (version 1).
        destination.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;

        // Gravity and Force settings
        destination.g = source.g;
        destination.maxTowardsForce = source.maxTowardsForce;
        destination.maxAwayFromForce = source.maxAwayFromForce;
        destination.gravityForceDamper = source.gravityForceDamper;
        destination.stopGravityDistance = source.stopGravityDistance;
        destination.stopMovingDistance = source.stopMovingDistance;
        destination.stopVelocity = source.stopVelocity;
        destination.attractionRadiusMultiplier = source.attractionRadiusMultiplier;

        // Curves
        destination.forceToMiddle = new AnimationCurve(source.forceToMiddle.keys);

        // Hand interaction settings
        destination.singleHandOpenForceDamper = source.singleHandOpenForceDamper;
        destination.pushForce = source.pushForce;
        destination.torsoMaxForwardOffset = source.torsoMaxForwardOffset;
        destination.torsoOffsetFalloffDistance = source.torsoOffsetFalloffDistance;
        destination.minDrag = source.minDrag;
        destination.maxDrag = source.maxDrag;
        destination.addedBoundaryDistance = source.addedBoundaryDistance;
        destination.boundaryOutwardDrag = source.boundaryOutwardDrag;
        destination.outOfBoundsResetDelay = source.outOfBoundsResetDelay;
        destination.alignmentVectorStrength = new AnimationCurve(
            source.alignmentVectorStrength.keys
        );
        destination.alignmentVectorStrengthScaler = source.alignmentVectorStrengthScaler;
        destination.handPushScaler = source.handPushScaler;
        destination.prayToActivate = source.prayToActivate;
        destination.prayToActivateDistance = source.prayToActivateDistance;

        // Pulsation settings
        destination.pulseAmount = source.pulseAmount;
        destination.pulseSpeed = source.pulseSpeed;
        destination.graphLimit = source.graphLimit;
        destination.pulseFreqs = source.pulseFreqs;

        // Size and scaling settings
        destination.singleHandScaling = source.singleHandScaling;
        destination.minimumUnscaledSize = source.minimumUnscaledSize;
        destination.maximumUnscaledSize = source.maximumUnscaledSize;
        destination.minHandDisplacementPerFrame = source.minHandDisplacementPerFrame;
        destination.maxHandVelocity = source.maxHandVelocity;
        destination.distanceDamper = new AnimationCurve(source.distanceDamper.keys);
        destination.pulseScaleDamper = source.pulseScaleDamper;
        destination.mergeSizeScalerDamper = source.mergeSizeScalerDamper;
        destination.maxDistanceBetweenHands = source.maxDistanceBetweenHands;
        destination.baseZDepth = source.baseZDepth;
        destination.gridScale = source.gridScale;
        destination.defaultUnscaledSize = source.defaultUnscaledSize;
        destination.bodyScale = source.bodyScale;
        destination.maxDistanceFromCamera = source.maxDistanceFromCamera;
        destination.sphereResetJitter = source.sphereResetJitter;

        // Hand VFX (nested group, copied as one object)
        destination.handVfx =
            source.handVfx != null ? source.handVfx.DeepCopy() : new HandVfxSettings();

        // Animation
        destination.particleInitializationDelay = source.particleInitializationDelay;
        destination.initializationResetDelay = source.initializationResetDelay;
        destination.singleHandOpenThreshold = source.singleHandOpenThreshold;
        destination.singleHandForceLerpDuration = source.singleHandForceLerpDuration;
        destination.initializationSpeed = source.initializationSpeed;
        destination.metaballRadiusAnimationDuration = source.metaballRadiusAnimationDuration;
        destination.metaballRadiusAnimationStartSize = source.metaballRadiusAnimationStartSize;
        destination.bodySpawnSize = source.bodySpawnSize;
        destination.metaballRadiusAnimationCurve = new AnimationCurve(
            source.metaballRadiusAnimationCurve.keys
        );

        // Style settings
        destination.customColors = source.customColors;
        destination.drawSkeleton = source.drawSkeleton;
        destination.useTrackingStateColors = source.useTrackingStateColors;

        // Debug settings
        destination.dummyOnlyMode = source.dummyOnlyMode;
        destination.showSphereMeshOnHandCollision = source.showSphereMeshOnHandCollision;
        destination.alwaysShowSphereMesh = source.alwaysShowSphereMesh;
        destination.showMetaballMesh = source.showMetaballMesh;
        destination.showPointCloud = source.showPointCloud;
        destination.showMetaballBounds = source.showMetaballBounds;
        destination.showAttractionRadius = source.showAttractionRadius;
        destination.showHandTrailDistorters = source.showHandTrailDistorters;
        destination.showSecondaryAttractor = source.showSecondaryAttractor;

        // Explicitly set all post-processing values to zero/defaults to prevent them from being saved in scene profiles
        destination.bloomThreshold = 0.0f;
        destination.bloomIntensity = 0.0f;
        destination.bloomScatter = 0.0f;
        destination.lensFlareIntensity = 0.0f;
        destination.lensFlareRegularMultiplier = 0.0f;
        destination.lensFlareReversedMultiplier = 0.0f;
        destination.lensFlareStreaksMultiplier = 0.0f;
        destination.lensFlareStreaksLength = 0.0f;
        destination.lensFlareStreaksOrientation = 0.0f;
        destination.lensFlareStreaksThreshold = 0.0f;
        destination.lensFlareChromaticIntensity = 0.0f;
        destination.lensDistortionIntensity = 0.0f;
        destination.lensDistortionXMultiplier = 0.0f;
        destination.lensDistortionYMultiplier = 0.0f;
        destination.lensDistortionScale = 0.0f;
        destination.lensDistortionCenterX = 0.0f;
        destination.lensDistortionCenterY = 0.0f;
        destination.colorAdjustmentsPostExposure = 0.0f;
        destination.colorAdjustmentsContrast = 0.0f;
        destination.colorAdjustmentsHueShift = 0.0f;
        destination.colorAdjustmentsSaturation = 0.0f;
        destination.whiteBalanceTemperature = 0.0f;
        destination.whiteBalanceTint = 0.0f;
    }

    private void CopyPostProcessingSettings(
        RuntimeSceneSettings source,
        RuntimeSceneSettings destination
    )
    {
        // Copy only post-processing settings to destination
        destination.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;

        // Bloom settings
        destination.bloomThreshold = source.bloomThreshold;
        destination.bloomIntensity = source.bloomIntensity;
        destination.bloomScatter = source.bloomScatter;

        // Lens Flare settings
        destination.lensFlareIntensity = source.lensFlareIntensity;
        destination.lensFlareRegularMultiplier = source.lensFlareRegularMultiplier;
        destination.lensFlareReversedMultiplier = source.lensFlareReversedMultiplier;
        destination.lensFlareStreaksMultiplier = source.lensFlareStreaksMultiplier;
        destination.lensFlareStreaksLength = source.lensFlareStreaksLength;
        destination.lensFlareStreaksOrientation = source.lensFlareStreaksOrientation;
        destination.lensFlareStreaksThreshold = source.lensFlareStreaksThreshold;
        destination.lensFlareChromaticIntensity = source.lensFlareChromaticIntensity;

        // Lens Distortion settings
        destination.lensDistortionIntensity = source.lensDistortionIntensity;
        destination.lensDistortionXMultiplier = source.lensDistortionXMultiplier;
        destination.lensDistortionYMultiplier = source.lensDistortionYMultiplier;
        destination.lensDistortionScale = source.lensDistortionScale;
        destination.lensDistortionCenterX = source.lensDistortionCenterX;
        destination.lensDistortionCenterY = source.lensDistortionCenterY;

        // Color Adjustments settings
        destination.colorAdjustmentsPostExposure = source.colorAdjustmentsPostExposure;
        destination.colorAdjustmentsContrast = source.colorAdjustmentsContrast;
        destination.colorAdjustmentsHueShift = source.colorAdjustmentsHueShift;
        destination.colorAdjustmentsSaturation = source.colorAdjustmentsSaturation;

        // White Balance settings
        destination.whiteBalanceTemperature = source.whiteBalanceTemperature;
        destination.whiteBalanceTint = source.whiteBalanceTint;

        // Explicitly set all scene-specific values to defaults to prevent them from being saved in post-processing profiles
        destination.g = 0.0f;
        destination.maxTowardsForce = 0.0f;
        destination.maxAwayFromForce = 0.0f;
        destination.gravityForceDamper = 0.0f;
        destination.stopGravityDistance = 0.0f;
        destination.stopMovingDistance = 0.0f;
        destination.stopVelocity = 0.0f;
        destination.attractionRadiusMultiplier = 0.0f;
        // Curves are scene settings, not post-processing
        destination.forceToMiddle = new AnimationCurve();
        destination.singleHandOpenForceDamper = 0.0f;
        destination.pushForce = 0.0f;
        destination.torsoMaxForwardOffset = 0.0f;
        destination.torsoOffsetFalloffDistance = 0.0f;
        destination.minDrag = 0.0f;
        destination.maxDrag = 0.0f;
        destination.addedBoundaryDistance = 0.0f;
        destination.boundaryOutwardDrag = 0.0f;
        destination.outOfBoundsResetDelay = 0.0f;
        destination.alignmentVectorStrength = new AnimationCurve();
        destination.alignmentVectorStrengthScaler = 0.0f;
        destination.handPushScaler = 0.0f;
        destination.prayToActivate = false;
        destination.prayToActivateDistance = 0.0f;
        destination.pulseAmount = 0.0f;
        destination.pulseSpeed = 0.0f;
        destination.graphLimit = 0.0f;
        destination.pulseFreqs = new float[0];
        destination.singleHandScaling = false;
        destination.minimumUnscaledSize = 0.0f;
        destination.maximumUnscaledSize = 0.0f;
        destination.minHandDisplacementPerFrame = 0.0f;
        destination.maxHandVelocity = 0.0f;
        destination.distanceDamper = new AnimationCurve();
        destination.pulseScaleDamper = 0.0f;
        destination.mergeSizeScalerDamper = 0.0f;
        destination.maxDistanceBetweenHands = 0.0f;
        destination.baseZDepth = 0.0f;
        destination.gridScale = 0.0f;
        destination.defaultUnscaledSize = 0.0f;
        destination.bodyScale = 0.0f;
        destination.maxDistanceFromCamera = 0.0f;
        destination.sphereResetJitter = 0.0f;
        // JsonUtility can't write null for a class field - PP files carry a defaults block
        // (MergePostProcessingSettings ignores it).
        destination.handVfx = new HandVfxSettings();
        destination.particleInitializationDelay = 0.0f;
        destination.initializationResetDelay = 0.0f;
        destination.singleHandOpenThreshold = 0.0f;
        destination.singleHandForceLerpDuration = 0.0f;
        destination.initializationSpeed = 0.0f;
        destination.metaballRadiusAnimationDuration = 0.0f;
        destination.metaballRadiusAnimationStartSize = 0.0f;
        destination.bodySpawnSize = 0.0f;
        destination.metaballRadiusAnimationCurve = new AnimationCurve();
        destination.dummyOnlyMode = false;
        destination.drawSkeleton = false;
        destination.customColors = false;
        destination.showSphereMeshOnHandCollision = false;
        destination.alwaysShowSphereMesh = false;
        destination.showMetaballMesh = false;
        destination.showPointCloud = false;
        destination.showMetaballBounds = false;
        destination.showAttractionRadius = false;
        destination.showHandTrailDistorters = false;
        destination.showSecondaryAttractor = false;
    }

    private void SaveCurrentProfile(TabType tabType)
    {
        if (tabType == TabType.Scene)
        {
            if (string.IsNullOrEmpty(currentSceneProfilePath))
            {
                ShowSaveAsDialog(tabType);
                return;
            }
            SaveProfile(currentSceneProfilePath, tabType);
        }
        else if (tabType == TabType.PostProcessing)
        {
            if (string.IsNullOrEmpty(currentPostProcessingProfilePath))
            {
                ShowSaveAsDialog(tabType);
                return;
            }
            SaveProfile(currentPostProcessingProfilePath, tabType);
        }
    }

    private void ShowSaveAsDialog(TabType tabType)
    {
        isModalOpen = true;

        // Create modal dialog for save as
        var modal = new VisualElement();
        modal.style.position = Position.Absolute;
        modal.style.left = 0;
        modal.style.top = 0;
        modal.style.right = 0;
        modal.style.bottom = 0;
        modal.style.backgroundColor = new Color(0, 0, 0, 0.8f);
        modal.style.alignItems = Align.Center;
        modal.style.justifyContent = Justify.Center;

        var panel = new VisualElement();
        panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        panel.style.borderTopColor = Color.gray;
        panel.style.borderBottomColor = Color.gray;
        panel.style.borderLeftColor = Color.gray;
        panel.style.borderRightColor = Color.gray;
        panel.style.paddingTop = 20;
        panel.style.paddingBottom = 20;
        panel.style.paddingLeft = 20;
        panel.style.paddingRight = 20;
        panel.style.width = 400;

        var title = new Label("Save Profile As...");
        title.style.fontSize = 18;
        title.style.color = Color.white;
        title.style.marginBottom = 15;
        panel.Add(title);

        var nameField = new TextField("Profile Name:");
        nameField.style.marginBottom = 15;
        nameField.value = $"Profile_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        nameField.SelectAll();
        panel.Add(nameField);

        var buttonContainer = new VisualElement();
        buttonContainer.style.flexDirection = FlexDirection.Row;
        buttonContainer.style.justifyContent = Justify.Center;
        buttonContainer.style.marginTop = 10;

        var saveButton = new Button(() =>
        {
            var profileName = nameField.value.Trim();
            if (!string.IsNullOrEmpty(profileName))
            {
                // Remove invalid filename characters
                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    profileName = profileName.Replace(c, '_');
                }

                SaveAsNewProfile(profileName, tabType);
                settingsPanel.Remove(modal);
                isModalOpen = false;
            }
        });
        saveButton.text = "Save";
        saveButton.style.marginRight = 10;
        saveButton.style.paddingLeft = 15;
        saveButton.style.paddingRight = 15;

        var cancelButton = new Button(() =>
        {
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        cancelButton.text = "Cancel";
        cancelButton.style.paddingLeft = 15;
        cancelButton.style.paddingRight = 15;

        buttonContainer.Add(saveButton);
        buttonContainer.Add(cancelButton);
        panel.Add(buttonContainer);

        modal.Add(panel);
        settingsPanel.Add(modal);

        // Focus the text field and select all text
        nameField.Focus();

        // Handle Enter key to save
        nameField.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                var profileName = nameField.value.Trim();
                if (!string.IsNullOrEmpty(profileName))
                {
                    // Remove invalid filename characters
                    foreach (var c in Path.GetInvalidFileNameChars())
                    {
                        profileName = profileName.Replace(c, '_');
                    }

                    SaveAsNewProfile(profileName, tabType);
                    settingsPanel.Remove(modal);
                    isModalOpen = false;
                }
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                settingsPanel.Remove(modal);
                isModalOpen = false;
            }
        });
    }

    private void SaveAsNewProfile(string profileName, TabType tabType)
    {
        string profilePath;
        string lastUsedKey;

        if (tabType == TabType.Scene)
        {
            profilePath = Path.Combine(sceneProfilesDirectory, profileName + ".json");
            lastUsedKey = lastUsedSceneProfileKey;
        }
        else
        {
            profilePath = Path.Combine(postProcessingProfilesDirectory, profileName + ".json");
            lastUsedKey = lastUsedPostProcessingProfileKey;
        }

        SaveProfile(profilePath, tabType);

        // Suppress profile loading during dropdown refresh
        isRefreshingSuppressed = true;
        RefreshProfileDropdowns();
        isRefreshingSuppressed = false;

        // Set dropdown value without triggering the callback (to avoid reloading the profile we just saved)
        if (tabType == TabType.Scene)
        {
            sceneProfileDropdown.SetValueWithoutNotify(profileName);
        }
        else
        {
            postProcessingProfileDropdown.SetValueWithoutNotify(profileName);
        }

        // Save as last used profile for this tab
        PlayerPrefs.SetString(lastUsedKey, profileName);
        PlayerPrefs.Save();
    }

    private void SaveProfile(string path, TabType tabType)
    {
        try
        {
            RuntimeSceneSettings settingsToSave;

            if (tabType == TabType.Scene)
            {
                // Create a clean settings object with only scene-related data
                settingsToSave = new RuntimeSceneSettings();
                // Important: Only copy scene settings, leave all post-processing settings at their default values
                CopySceneSettings(runtimeSettings, settingsToSave);
                currentSceneProfilePath = path;
            }
            else
            {
                // Create a settings object with only post-processing data
                settingsToSave = new RuntimeSceneSettings();
                CopyPostProcessingSettings(runtimeSettings, settingsToSave);
                currentPostProcessingProfilePath = path;

                // Update volume controller with post-processing settings
                if (Controller?.volumeController != null)
                {
                    Controller.volumeController.ApplyCurrentSettings(runtimeSettings);
                }
            }

            var json = JsonUtility.ToJson(settingsToSave, true);
            File.WriteAllText(path, json);

            // The saved profile is the new baseline for this tab.
            if (tabType == TabType.Scene)
                sceneBaselineJson = CanonicalSceneJson(runtimeSettings);
            else
                postProcessingBaselineJson = CanonicalPostProcessingJson(runtimeSettings);
            UpdateDirtyState();
            UpdateDirtyIndicators();
            SaveWorkingSet();
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save {tabType} profile: {e.Message}");
        }
    }

    public RuntimeSceneSettings GetCurrentSettings()
    {
        return runtimeSettings;
    }

    /// <summary>
    /// Update the menu's runtime settings and refresh the UI
    /// Called when inspector values change in SceneController
    /// </summary>
    public void UpdateSettingsFromInspector(RuntimeSceneSettings newSettings)
    {
        if (newSettings != null)
        {
            SetRuntimeSettings(newSettings.DeepCopy());

            // Only refresh UI if the panels are initialized (Start() has been called)
            if (sceneSettingsPanel != null && postProcessingPanel != null)
            {
                RefreshUI();
                UpdateDirtyState();
                UpdateDirtyIndicators();
                SaveWorkingSet();
            }
        }
    }

    // ---- Working set / dirty tracking ----

    private string ActiveSceneName =>
        UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

    private string CurrentSceneProfileName =>
        string.IsNullOrEmpty(currentSceneProfilePath)
            ? ""
            : Path.GetFileNameWithoutExtension(currentSceneProfilePath);

    private string CurrentPostProcessingProfileName =>
        string.IsNullOrEmpty(currentPostProcessingProfilePath)
            ? ""
            : Path.GetFileNameWithoutExtension(currentPostProcessingProfilePath);

    /// <summary>
    /// Replaces the base settings object and keeps the debugging-change subscription attached
    /// (the old code lost it whenever the object was swapped).
    /// </summary>
    private void SetRuntimeSettings(RuntimeSceneSettings settings)
    {
        if (runtimeSettings != null)
            runtimeSettings.OnAnyDebuggingSettingChanged -= OnDebuggingSettingChanged;
        runtimeSettings = settings;
        if (runtimeSettings != null)
            runtimeSettings.OnAnyDebuggingSettingChanged += OnDebuggingSettingChanged;
    }

    private void OnDebuggingSettingChanged() => NotifySettingsChanged();

    /// <summary>
    /// Single exit point for "the settings changed": persists the working set, refreshes the
    /// dirty markers and raises <see cref="OnSettingsChanged"/> for the controller.
    /// </summary>
    private void NotifySettingsChanged()
    {
        UpdateDirtyState();
        UpdateDirtyIndicators();
        SaveWorkingSet();
        OnSettingsChanged?.Invoke(runtimeSettings);
    }

    private void SaveWorkingSet()
    {
        if (runtimeSettings == null)
            return;
        SettingsWorkingSet.Save(
            ActiveSceneName,
            runtimeSettings,
            CurrentSceneProfileName,
            CurrentPostProcessingProfileName
        );
    }

    /// <summary>
    /// Loads the working set for this scene into <see cref="runtimeSettings"/> and rebuilds the
    /// dirty baselines from the profiles it names. Returns false when there is none.
    /// </summary>
    private bool TryRestoreWorkingSet()
    {
        var file = SettingsWorkingSet.Load(ActiveSceneName);
        if (file == null)
            return false;

        SetRuntimeSettings(file.settings.DeepCopy());

        currentSceneProfilePath = ResolveProfilePath(sceneProfilesDirectory, file.sceneProfileName);
        currentPostProcessingProfilePath = ResolveProfilePath(
            postProcessingProfilesDirectory,
            file.postProcessingProfileName
        );
        sceneBaselineJson = ComputeProfileBaseline(currentSceneProfilePath, ProfileType.Scene);
        postProcessingBaselineJson = ComputeProfileBaseline(
            currentPostProcessingProfilePath,
            ProfileType.PostProcessing
        );

        // Keep the last-used keys in step so a missing working set still falls back sensibly.
        if (!string.IsNullOrEmpty(currentSceneProfilePath))
            PlayerPrefs.SetString(lastUsedSceneProfileKey, file.sceneProfileName);
        if (!string.IsNullOrEmpty(currentPostProcessingProfilePath))
            PlayerPrefs.SetString(lastUsedPostProcessingProfileKey, file.postProcessingProfileName);
        PlayerPrefs.Save();

        Debug.Log(
            $"[InGameSettingsMenu] Restored working set for '{ActiveSceneName}' "
                + $"(scene profile '{file.sceneProfileName}', PP profile '{file.postProcessingProfileName}', saved {file.savedAtUtc})."
        );
        return true;
    }

    private static string ResolveProfilePath(string directory, string profileName)
    {
        if (string.IsNullOrEmpty(profileName))
            return "";
        string path = Path.Combine(directory, profileName + ".json");
        return File.Exists(path) ? path : "";
    }

    /// <summary>
    /// Canonical JSON of the given profile file as it would look once merged - i.e. exactly what
    /// a fresh load of it would produce - without disturbing the live settings.
    /// </summary>
    private string ComputeProfileBaseline(string path, ProfileType profileType)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return "";
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonUtility.FromJson<RuntimeSceneSettings>(json);
            if (
                profileType == ProfileType.Scene
                && loaded.settingsVersion < RuntimeSceneSettings.CurrentSettingsVersion
            )
            {
                BodyScaling.ConvertLegacyProfileInPlace(loaded, json);
            }

            // Merge into a scratch copy so the merge code stays the single source of truth.
            var live = runtimeSettings;
            runtimeSettings = live.DeepCopy();
            string baseline;
            if (profileType == ProfileType.Scene)
            {
                MergeSceneSettings(loaded);
                baseline = CanonicalSceneJson(runtimeSettings);
            }
            else
            {
                MergePostProcessingSettings(loaded);
                baseline = CanonicalPostProcessingJson(runtimeSettings);
            }
            runtimeSettings = live;
            return baseline;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[InGameSettingsMenu] Could not read baseline '{path}': {e.Message}");
            return "";
        }
    }

    private string CanonicalSceneJson(RuntimeSceneSettings source)
    {
        var clean = new RuntimeSceneSettings();
        CopySceneSettings(source, clean);
        return JsonUtility.ToJson(clean);
    }

    private string CanonicalPostProcessingJson(RuntimeSceneSettings source)
    {
        var clean = new RuntimeSceneSettings();
        CopyPostProcessingSettings(source, clean);
        return JsonUtility.ToJson(clean);
    }

    private void UpdateDirtyState()
    {
        if (runtimeSettings == null)
            return;
        isSceneDirty =
            string.IsNullOrEmpty(currentSceneProfilePath)
            || CanonicalSceneJson(runtimeSettings) != sceneBaselineJson;
        isPostProcessingDirty =
            string.IsNullOrEmpty(currentPostProcessingProfilePath)
            || CanonicalPostProcessingJson(runtimeSettings) != postProcessingBaselineJson;
    }

    private void UpdateDirtyIndicators()
    {
        SetDirtyLabel(sceneDirtyLabel, isSceneDirty, currentSceneProfilePath);
        SetDirtyLabel(
            postProcessingDirtyLabel,
            isPostProcessingDirty,
            currentPostProcessingProfilePath
        );
        if (sceneTab != null)
            sceneTab.text = isSceneDirty ? "Scene *" : "Scene";
        if (postProcessingTab != null)
            postProcessingTab.text = isPostProcessingDirty
                ? "Post Processing *"
                : "Post Processing";
    }

    private static void SetDirtyLabel(Label label, bool dirty, string profilePath)
    {
        if (label == null)
            return;
        label.style.display = dirty ? DisplayStyle.Flex : DisplayStyle.None;
        label.text = string.IsNullOrEmpty(profilePath) ? "Unsaved (no profile)" : "Unsaved changes";
    }

    public bool IsSceneDirty => isSceneDirty;
    public bool IsPostProcessingDirty => isPostProcessingDirty;

    /// <summary>
    /// Load the dropdown's profile, asking first when the tab has unsaved changes.
    /// <paramref name="previousDropdownValue"/> is restored on cancel (dropdown-driven loads).
    /// </summary>
    private void RequestLoadSelectedProfile(TabType tabType, string previousDropdownValue = null)
    {
        bool dirty = tabType == TabType.Scene ? isSceneDirty : isPostProcessingDirty;
        string tabKey = tabType == TabType.Scene ? "scene" : "postprocessing";
        if (!dirty)
        {
            LoadSelectedProfile(tabKey);
            return;
        }

        string activeName =
            tabType == TabType.Scene ? CurrentSceneProfileName : CurrentPostProcessingProfileName;
        string message = string.IsNullOrEmpty(activeName)
            ? "The current settings have not been saved to a profile. Loading will discard them."
            : $"'{activeName}' has unsaved changes. Loading will discard them.";

        ShowConfirmDialog(
            "Discard unsaved changes?",
            message,
            "Discard & Load",
            onConfirm: () => LoadSelectedProfile(tabKey),
            onCancel: () =>
            {
                if (previousDropdownValue == null)
                    return;
                var dropdown =
                    tabType == TabType.Scene ? sceneProfileDropdown : postProcessingProfileDropdown;
                dropdown?.SetValueWithoutNotify(previousDropdownValue);
            }
        );
    }

    private void ShowConfirmDialog(
        string titleText,
        string messageText,
        string confirmText,
        Action onConfirm,
        Action onCancel
    )
    {
        isModalOpen = true;

        var modal = new VisualElement();
        modal.style.position = Position.Absolute;
        modal.style.left = 0;
        modal.style.top = 0;
        modal.style.right = 0;
        modal.style.bottom = 0;
        modal.style.backgroundColor = new Color(0, 0, 0, 0.8f);
        modal.style.alignItems = Align.Center;
        modal.style.justifyContent = Justify.Center;

        var panel = new VisualElement();
        panel.style.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        panel.style.borderTopWidth = 2;
        panel.style.borderBottomWidth = 2;
        panel.style.borderLeftWidth = 2;
        panel.style.borderRightWidth = 2;
        panel.style.borderTopColor = Color.gray;
        panel.style.borderBottomColor = Color.gray;
        panel.style.borderLeftColor = Color.gray;
        panel.style.borderRightColor = Color.gray;
        panel.style.paddingTop = 20;
        panel.style.paddingBottom = 20;
        panel.style.paddingLeft = 20;
        panel.style.paddingRight = 20;
        panel.style.width = 440;

        var title = new Label(titleText);
        title.style.fontSize = 18;
        title.style.color = Color.white;
        title.style.marginBottom = 10;
        panel.Add(title);

        var message = new Label(messageText);
        message.style.color = new Color(0.85f, 0.85f, 0.85f);
        message.style.whiteSpace = WhiteSpace.Normal;
        message.style.marginBottom = 15;
        panel.Add(message);

        var buttons = new VisualElement();
        buttons.style.flexDirection = FlexDirection.Row;
        buttons.style.justifyContent = Justify.Center;

        void Close()
        {
            settingsPanel.Remove(modal);
            isModalOpen = false;
        }

        var confirm = new Button(() =>
        {
            Close();
            onConfirm?.Invoke();
        });
        confirm.text = confirmText;
        confirm.style.marginRight = 10;
        confirm.style.paddingLeft = 15;
        confirm.style.paddingRight = 15;

        var cancel = new Button(() =>
        {
            Close();
            onCancel?.Invoke();
        });
        cancel.text = "Cancel";
        cancel.style.paddingLeft = 15;
        cancel.style.paddingRight = 15;

        buttons.Add(confirm);
        buttons.Add(cancel);
        panel.Add(buttons);
        modal.Add(panel);
        settingsPanel.Add(modal);

        modal.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                Close();
                onCancel?.Invoke();
            }
        });
        cancel.Focus();
    }
}
