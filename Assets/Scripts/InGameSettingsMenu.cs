using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RuntimeColorEditor;
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

    /// <summary>
    /// True when the scene has <paramref name="feature"/> (Kinect, camera feed). Settings that
    /// depend on one are only built when it's present. Without a controller nothing is hidden.
    /// </summary>
    private bool SceneSupports(SceneController.SceneFeature feature) =>
        Controller == null || Controller.HasFeature(feature);

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

    // Dirty tracking: canonical JSON of the scene / PP slice as it was when the active profile
    // was last loaded or saved. Dirty = current canonical JSON differs from this.
    private string sceneBaselineJson = "";
    private bool isSceneDirty;
    private Label sceneDirtyLabel;
    private Label postProcessingDirtyLabel;

    // True when Start() restored the working set - suppresses the last-used-profile auto-load.
    private bool restoredFromWorkingSet;
    private string sceneProfilesDirectory;
    private string postProcessingProfilesDirectory;
    private string lastUsedSceneProfileKey = "LastUsedSceneProfile";

    /// <summary>
    /// Post-processing has two targets - the particle layer and the camera feed - each with its
    /// own look, active profile, dirty baseline and last-used key. Any PP profile file loads
    /// into either one; the Post Processing tab edits whichever is selected.
    /// </summary>
    public enum PostProcessingTarget
    {
        Particles,
        Feed,
    }

    private class PostProcessingTargetState
    {
        public string displayName;
        public string profilePath = "";
        public string baselineJson = "";
        public bool isDirty;
        public string lastUsedKey;
        public Func<RuntimeSceneSettings, PostProcessSettings> get;
        public Action<RuntimeSceneSettings, PostProcessSettings> set;

        public string ProfileName =>
            string.IsNullOrEmpty(profilePath) ? "" : Path.GetFileNameWithoutExtension(profilePath);
    }

    private readonly PostProcessingTargetState particlePP = new()
    {
        displayName = "Particles",
        lastUsedKey = "LastUsedPostProcessingProfile",
        get = s => s.particlePostProcessing ??= new PostProcessSettings(),
        set = (s, v) => s.particlePostProcessing = v,
    };

    private readonly PostProcessingTargetState feedPP = new()
    {
        displayName = "Camera Feed",
        lastUsedKey = "LastUsedFeedPostProcessingProfile",
        get = s => s.feedPostProcessing ??= new PostProcessSettings(),
        set = (s, v) => s.feedPostProcessing = v,
    };

    private PostProcessingTarget activePostProcessingTarget = PostProcessingTarget.Particles;
    private Button postProcessingTargetParticlesButton,
        postProcessingTargetFeedButton;

    private PostProcessingTargetState GetPPState(PostProcessingTarget target) =>
        target == PostProcessingTarget.Feed ? feedPP : particlePP;

    private PostProcessingTargetState ActivePPState => GetPPState(activePostProcessingTarget);

    /// <summary>
    /// The Camera Feed target only exists while there is a feed to post-process: the scene has a
    /// feed volume and a camera feed, and Show Camera Feed is on. Its values and profile are kept
    /// while it's unavailable; they just aren't editable and don't count as unsaved changes.
    /// </summary>
    private bool IsFeedTargetAvailable =>
        Controller?.volumeController?.feedVolume != null
        && SceneSupports(SceneController.SceneFeature.CameraFeed)
        && runtimeSettings != null
        && runtimeSettings.showCameraFeed;

    /// <summary>The look the Post Processing tab's fields read and write.</summary>
    private PostProcessSettings ActivePP => ActivePPState.get(runtimeSettings);

    /// <summary>
    /// A collapsible settings group. <see cref="content"/> holds its rows; the header toggles it.
    /// Rebuilt with the UI, while the collapsed state lives in <see cref="collapsedGroups"/>.
    /// </summary>
    private class SettingGroup
    {
        public string key;
        public string title;
        public ScrollView panel;
        public SettingSection section; // null for groups placed straight in a panel
        public VisualElement root;
        public VisualElement content;
    }

    /// <summary>
    /// A page of the Scene tab (Space, Kinect, Ball, ...) holding its groups in
    /// <see cref="content"/>, picked from the sidebar by <see cref="navItem"/>. One page shows at
    /// a time; while searching, every page with matches is listed. Rebuilt with the UI, like
    /// <see cref="SettingGroup"/>.
    /// </summary>
    private class SettingSection
    {
        public string key;
        public string title;
        public ScrollView panel;
        public VisualElement root;
        public VisualElement content;
        public Button navItem;
    }

    private readonly List<SettingGroup> settingGroups = new();
    private readonly List<SettingSection> settingSections = new();

    // Keys of collapsed groups (section or panel key + "/" + title). Kept across UI rebuilds and
    // sessions.
    private readonly HashSet<string> collapsedGroups = new();
    private const string CollapsedGroupsPrefKey = "SettingsMenuCollapsedGroups";

    // Title of the Scene tab page shown when not searching. Kept across UI rebuilds and sessions.
    private string selectedSection;
    private const string SelectedSectionPrefKey = "SettingsMenuSelectedSection";
    private VisualElement sceneSectionNav;

    // Per-tab search: filters that tab's rows by label (or whole groups by title).
    private TextField sceneSearchField,
        postProcessingSearchField;
    private readonly Dictionary<ScrollView, Label> searchEmptyLabels = new();

    // Each setting row by (group content element, label), for ShowRowIf. Labels repeat across
    // groups ("Stick Force", "Size Range"), so a label alone isn't a key.
    private readonly Dictionary<(VisualElement group, string label), VisualElement> settingRows =
        new();

    // Rows whose visibility depends on other settings (the menu's counterpart to the inspector's
    // NaughtyAttributes ShowIf). Re-evaluated on every settings change; rebuilt with the UI.
    private readonly List<(VisualElement row, Func<bool> isVisible)> conditionalRows = new();
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

        var collapsed = PlayerPrefs.GetString(CollapsedGroupsPrefKey, "");
        foreach (var key in collapsed.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            collapsedGroups.Add(key);
        selectedSection = PlayerPrefs.GetString(SelectedSectionPrefKey, "");
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
            particlePP.lastUsedKey = Controller.GetSceneSpecificPostProcessingProfileKey();
            feedPP.lastUsedKey = Controller.GetSceneSpecificFeedPostProcessingProfileKey();
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
                || particlePP.lastUsedKey == "LastUsedPostProcessingProfile"
                || feedPP.lastUsedKey == "LastUsedFeedPostProcessingProfile"
            )
        )
        {
            lastUsedSceneProfileKey = Controller.GetSceneSpecificSceneProfileKey();
            particlePP.lastUsedKey = Controller.GetSceneSpecificPostProcessingProfileKey();
            feedPP.lastUsedKey = Controller.GetSceneSpecificFeedPostProcessingProfileKey();
        }
    }

    private void Update()
    {
        if (
            Input.GetKeyDown(KeyCode.M)
            && !isModalOpen
            && !IsAnyPopupVisible
            && !IsTextFieldFocused()
        )
        {
            ToggleMenu();
        }

        // Toggle the invisible blocker overlay so UI Toolkit elements can't be
        // interacted with while an IMGUI popup (curve / gradient / color) is visible.
        if (curveEditorBlocker != null)
        {
            curveEditorBlocker.style.display = IsAnyPopupVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }
    }

    private static bool IsAnyPopupVisible =>
        RuntimeCurveEditorWindow.IsVisible
        || RuntimeGradientEditorWindow.IsVisible
        || RuntimeColorPickerWindow.IsVisible;

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

        // Invisible overlay that blocks all UI Toolkit interaction when an
        // IMGUI popup is open (the two input systems are independent).
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
            sceneSearchField = sceneTabContent.Q<TextField>("SceneSearchField");
            sceneSectionNav = sceneTabContent.Q<VisualElement>("SceneSectionNav");
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
            postProcessingTargetParticlesButton = postProcessingTabContent.Q<Button>(
                "PostProcessingTargetParticles"
            );
            postProcessingTargetFeedButton = postProcessingTabContent.Q<Button>(
                "PostProcessingTargetFeed"
            );
            postProcessingSearchField = postProcessingTabContent.Q<TextField>(
                "PostProcessingSearchField"
            );
        }

        SetupSearchField(sceneSearchField, sceneSettingsPanel);
        SetupSearchField(postProcessingSearchField, postProcessingPanel);

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

        if (postProcessingTargetParticlesButton != null)
            postProcessingTargetParticlesButton.clicked += () =>
                SwitchPostProcessingTarget(PostProcessingTarget.Particles);
        if (postProcessingTargetFeedButton != null)
        {
            postProcessingTargetFeedButton.clicked += () =>
                SwitchPostProcessingTarget(PostProcessingTarget.Feed);
            // The switch row is shown / hidden by UpdatePostProcessingTargetAvailability.
        }

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
        sceneSectionNav?.Clear();
        settingRows.Clear();
        conditionalRows.Clear();
        settingGroups.Clear();
        settingSections.Clear();
        searchEmptyLabels.Clear();

        // Scene Settings Tab
        CreateSceneSettingsContent();

        // Post Processing Tab
        CreatePostProcessingContent();

        // The search fields live outside the rebuilt panels, so their queries survive a rebuild.
        AddSearchEmptyLabel(sceneSettingsPanel);
        AddSearchEmptyLabel(postProcessingPanel);
        ApplyAllSearches();
    }

    // Section and group names (and the row labels) match the [SettingGroup] / [Label]
    // attributes on the SceneController inspector twins and HandVfxSettings, so the inspector
    // shows the same layout. Labels may repeat across groups ("Stick Force"); search matches the
    // section and group titles too, so "trail stick" narrows to one of them.
    private void CreateSceneSettingsContent()
    {
        var panel = sceneSettingsPanel;

        var space = CreateSection(SettingSections.Space, panel, sceneSectionNav);
        CreateWorldGroup(space);
        CreatePlayBoundaryGroup(space);

        // Feature-dependent settings are hidden (not dropped) where the scene lacks the feature:
        // their values still load and save with the profile, so shared profiles keep them.
        if (SceneSupports(SceneController.SceneFeature.Kinect))
        {
            var kinect = CreateSection(SettingSections.Kinect, panel, sceneSectionNav);
            CreateTrackingGroup(kinect);
            if (SceneSupports(SceneController.SceneFeature.CameraFeed))
                CreateCameraFeedGroup(kinect);
            CreateSkeletonGroup(kinect);
        }

        var ball = CreateSection(SettingSections.Ball, panel, sceneSectionNav);
        CreateBallSizeGroup(ball);
        CreateBreathingGroup(ball);
        CreateSpawnGroup(ball);
        CreateGravityGroup(ball);

        var hands = CreateSection(SettingSections.Hands, panel, sceneSectionNav);
        CreateActivationGroup(hands);
        CreatePushGroup(hands);
        CreateAimGroup(hands);
        CreateOneHandGroup(hands);
        CreateGrowShrinkGroup(hands);

        var particles = CreateSection(SettingSections.Particles, panel, sceneSectionNav);
        CreateParticleColorGroup(particles);
        CreateGlowGroup(particles);
        CreateEmissionGroup(particles);
        CreateLifetimeGroup(particles);
        CreateStretchGroup(particles);
        CreateBallAttractionGroup(particles);
        CreateSecondaryAttractorGroup(particles);
        CreateTrailDistortersGroup(particles);
        CreateNoiseGroup(particles);
        CreateHiHatBurstsGroup(particles);
        CreateSnareBurstsGroup(particles);

        var debug = CreateSection(SettingSections.Debug, panel, sceneSectionNav);
        CreateVisualizersGroup(debug);
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

    private void SwitchPostProcessingTarget(PostProcessingTarget target)
    {
        if (target == activePostProcessingTarget)
            return;
        activePostProcessingTarget = target;

        postProcessingTargetParticlesButton?.EnableInClassList(
            "active",
            target == PostProcessingTarget.Particles
        );
        postProcessingTargetFeedButton?.EnableInClassList(
            "active",
            target == PostProcessingTarget.Feed
        );

        // Point the dropdown at this target's profile and rebind the fields to its look.
        postProcessingProfileDropdown?.SetValueWithoutNotify(ActivePPState.ProfileName);
        RefreshUI();
        UpdateDirtyIndicators();
    }

    // ---- Space ----

    private void CreateWorldGroup(SettingSection section)
    {
        var group = CreateGroup("World", section);

        CreateFloatField(
            group,
            "Body Scale",
            () => runtimeSettings.bodyScale,
            v => runtimeSettings.bodyScale = v,
            tooltip: "World scale of the Kinect space. Every size-, speed- or force-related setting is stored at 1x and scaled by this at runtime, so changing it alone keeps gameplay and look identical relative to the body."
        );
        CreateFloatField(
            group,
            "Base Z Depth",
            () => runtimeSettings.baseZDepth,
            v => runtimeSettings.baseZDepth = v,
            tooltip: "World-space depth of the play volume: the metaball grid, boundary and Kinect joints are all placed at this Z. Also sent to the hand VFX."
        );
        CreateFloatField(
            group,
            "Grid Scale",
            () => runtimeSettings.gridScale,
            v => runtimeSettings.gridScale = v,
            tooltip: "World size of one marching-cubes voxel (volume = 64x32x64 voxels). Scales with bodyScale."
        );
    }

    private void CreatePlayBoundaryGroup(SettingSection section)
    {
        var group = CreateGroup("Play Boundary", section);

        CreateFloatField(
            group,
            "Margin",
            () => runtimeSettings.addedBoundaryDistance,
            v => runtimeSettings.addedBoundaryDistance = v,
            tooltip: "Margin added around the metaball grid to define the play boundary. Beyond it Outward Drag engages and the ball becomes eligible for reset."
        );
        CreateFloatField(
            group,
            "Outward Drag",
            () => runtimeSettings.boundaryOutwardDrag,
            v => runtimeSettings.boundaryOutwardDrag = v,
            tooltip: "Drag that opposes the ball while it is past the boundary and moving away from the hands. 0 disables."
        );
        CreateFloatField(
            group,
            "Reset Delay",
            () => runtimeSettings.outOfBoundsResetDelay,
            v => runtimeSettings.outOfBoundsResetDelay = v,
            tooltip: "Seconds the ball must stay out of bounds before opening both hands snaps it back to the hand midpoint (plus Reset Jitter)."
        );
        CreateFloatField(
            group,
            "Reset Jitter",
            () => runtimeSettings.sphereResetJitter,
            v => runtimeSettings.sphereResetJitter = v,
            tooltip: "Random +/- offset added when the ball is reset to the hand midpoint, so overlapping balls don't reset to exactly the same spot."
        );
    }

    // ---- Kinect (only built when the scene has a Kinect) ----

    private void CreateTrackingGroup(SettingSection section)
    {
        var group = CreateGroup("Tracking", section);

        CreateFloatField(
            group,
            "Max Player Distance",
            () => runtimeSettings.maxDistanceFromCamera,
            v => runtimeSettings.maxDistanceFromCamera = v,
            tooltip: "If a player's hands are tracked farther from the camera than this, they are treated as closed and their colliders/skeleton lines are disabled (filters people far in the background)."
        );
    }

    private void CreateCameraFeedGroup(SettingSection section)
    {
        var group = CreateGroup("Camera Feed", section);

        CreateToggleField(
            group,
            "Show Camera Feed",
            () => runtimeSettings.showCameraFeed,
            v => runtimeSettings.showCameraFeed = v,
            tooltip: "Show the Kinect color feed behind the players. Off leaves a black background."
        );
    }

    private void CreateSkeletonGroup(SettingSection section)
    {
        var group = CreateGroup("Skeleton", section);

        CreateToggleField(
            group,
            "Draw Skeleton",
            () => runtimeSettings.drawSkeleton,
            v => runtimeSettings.drawSkeleton = v,
            tooltip: "Draw line-renderer bones between tracked Kinect joints for each player."
        );
        CreateToggleField(
            group,
            "Tracking State Colors",
            () => runtimeSettings.useTrackingStateColors,
            v => runtimeSettings.useTrackingStateColors = v,
            tooltip: "Color skeleton bones by Kinect joint tracking state (tracked / inferred / not tracked) instead of the player's color."
        );
        ShowRowIf(group, "Tracking State Colors", () => runtimeSettings.drawSkeleton);

        CreateColorField(
            group,
            "Skeleton Color",
            () => runtimeSettings.skeletonColor,
            v => runtimeSettings.skeletonColor = v,
            tooltip: "Bone color for every player while Color Per Player (Particles > Color) is off."
        );
        ShowRowIf(
            group,
            "Skeleton Color",
            () =>
                runtimeSettings.drawSkeleton
                && !runtimeSettings.useTrackingStateColors
                && !runtimeSettings.individualColors
        );

        CreateColorArrayField(
            group,
            "Skeleton Palette",
            () => runtimeSettings.skeletonColors,
            v => runtimeSettings.skeletonColors = v,
            tooltip: "Skeleton color for each Color Per Player slot (same index as the Particle Palette). Wraps around when shorter than the Particle Palette; empty falls back to the single Skeleton Color."
        );
        ShowRowIf(
            group,
            "Skeleton Palette",
            () =>
                runtimeSettings.drawSkeleton
                && !runtimeSettings.useTrackingStateColors
                && runtimeSettings.individualColors
        );
    }

    // ---- Ball ----

    private void CreateBallSizeGroup(SettingSection section)
    {
        var group = CreateGroup("Size", section);

        CreateFloatField(
            group,
            "Start Size",
            () => runtimeSettings.defaultUnscaledSize,
            v => runtimeSettings.defaultUnscaledSize = v,
            tooltip: "Starting diameter of a new player's ball before any pulsation, growing or shrinking is applied."
        );
        CreateFloatField(
            group,
            "Min Size",
            () => runtimeSettings.minimumUnscaledSize,
            v => runtimeSettings.minimumUnscaledSize = v,
            tooltip: "The minimum size the ball can shrink to."
        );
        CreateFloatField(
            group,
            "Max Size",
            () => runtimeSettings.maximumUnscaledSize,
            v => runtimeSettings.maximumUnscaledSize = v,
            tooltip: "The maximum size the ball can grow to."
        );
        CreateFloatField(
            group,
            "Merge Size Damper",
            () => runtimeSettings.mergeSizeScalerDamper,
            v => runtimeSettings.mergeSizeScalerDamper = v,
            tooltip: "A damper for the scaling that occurs when multiple balls merge together. 0 disables merging."
        );
    }

    private void CreateBreathingGroup(SettingSection section)
    {
        var group = CreateGroup("Breathing", section);

        CreateSliderField(
            group,
            "Amount",
            () => runtimeSettings.pulseAmount,
            v => runtimeSettings.pulseAmount = v,
            0f,
            10f,
            tooltip: "Amplitude of the idle 'breathing' size wobble, as a fraction of the ball's size (value/10). 0 disables it."
        );
        CreateFloatField(
            group,
            "Speed",
            () => runtimeSettings.pulseSpeed,
            v => runtimeSettings.pulseSpeed = v,
            tooltip: "Time multiplier for the breathing wobble. Higher = faster oscillation."
        );
        CreateFloatField(
            group,
            "Graph Limit",
            () => runtimeSettings.graphLimit,
            v => runtimeSettings.graphLimit = v,
            tooltip: "Expected peak of the summed sine waves, used to normalize the wobble into 0..Amount. Roughly the number of Frequencies entries; lower values clip, higher values flatten the pulse."
        );
        CreateFloatArrayField(
            group,
            "Frequencies",
            () => runtimeSettings.pulseFreqs,
            v => runtimeSettings.pulseFreqs = v,
            tooltip: "Frequencies of the sine waves summed to make the breathing wobble (y = sin(f1*t) + sin(f2*t) + ...). Mixed, non-integer values give a less regular pulse."
        );
    }

    private void CreateSpawnGroup(SettingSection section)
    {
        var group = CreateGroup("Spawn", section);

        CreateFloatField(
            group,
            "Grow-In Duration",
            () => runtimeSettings.metaballRadiusAnimationDuration,
            v => runtimeSettings.metaballRadiusAnimationDuration = v,
            tooltip: "Seconds for the metaball radius to grow from Grow-In Start Radius to full size when a player initializes."
        );
        CreateFloatField(
            group,
            "Grow-In Start Radius",
            () => runtimeSettings.metaballRadiusAnimationStartSize,
            v => runtimeSettings.metaballRadiusAnimationStartSize = v,
            tooltip: "Metaball radius the grow-in starts from."
        );
        CreateCurveField(
            group,
            "Grow-In Curve",
            () => runtimeSettings.metaballRadiusAnimationCurve,
            v => runtimeSettings.metaballRadiusAnimationCurve = v,
            tooltip: "Easing curve for the metaball grow-in (X: 0-1 normalized time, Y: 0-1 progress from start radius to full size)."
        );
        CreateFloatField(
            group,
            "Spawn Flash Size",
            () => runtimeSettings.bodySpawnSize,
            v => runtimeSettings.bodySpawnSize = v,
            tooltip: "Particle size of the BodyEffects.vfx spawn flash on VFX_Body."
        );
    }

    private void CreateGravityGroup(SettingSection section)
    {
        var group = CreateGroup("Gravity", section);

        CreateFloatField(
            group,
            "G",
            () => runtimeSettings.g,
            v => runtimeSettings.g = v,
            tooltip: "Gravitational constant of the pairwise attraction between balls."
        );
        CreateFloatField(
            group,
            "Max Towards Force",
            () => runtimeSettings.maxTowardsForce,
            v => runtimeSettings.maxTowardsForce = v,
            tooltip: "Cap on the pairwise gravity force (G*m1*m2/r^2) while two balls are moving toward each other. Keeps close balls from slamming together."
        );
        CreateFloatField(
            group,
            "Max Away Force",
            () => runtimeSettings.maxAwayFromForce,
            v => runtimeSettings.maxAwayFromForce = v,
            tooltip: "Cap on the pairwise gravity force while two balls are moving apart. Setting it above Max Towards Force lets gravity resist separation more than it accelerates approach."
        );
        CreateFloatField(
            group,
            "Damper",
            () => runtimeSettings.gravityForceDamper,
            v => runtimeSettings.gravityForceDamper = v,
            tooltip: "Multiplier applied to Max Towards Force when that cap kicks in. Below 1 softens the final approach; 1 = no extra damping."
        );
        CreateFloatField(
            group,
            "Stop Gravity Distance",
            () => runtimeSettings.stopGravityDistance,
            v => runtimeSettings.stopGravityDistance = v,
            tooltip: "Center-to-center distance below which gravity stops being applied. Inside this range the balls coast (or get stopped, see Stop Moving Distance)."
        );
        CreateFloatField(
            group,
            "Stop Moving Distance",
            () => runtimeSettings.stopMovingDistance,
            v => runtimeSettings.stopMovingDistance = v,
            tooltip: "Within this center-to-center distance, if the balls' relative speed is below Stop Velocity, a counter-force cancels their motion so they settle side by side."
        );
        CreateFloatField(
            group,
            "Stop Velocity",
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

    // ---- Hands ----

    private void CreateActivationGroup(SettingSection section)
    {
        var group = CreateGroup("Activation", section);

        CreateToggleField(
            group,
            "Pray To Activate",
            () => runtimeSettings.prayToActivate,
            v => runtimeSettings.prayToActivate = v,
            tooltip: "When enabled, players must bring their hands together to initialize. When disabled, players start initialized."
        );
        CreateFloatField(
            group,
            "Pray Distance",
            () => runtimeSettings.prayToActivateDistance,
            v => runtimeSettings.prayToActivateDistance = v,
            tooltip: "How close the hands must come to activate the player."
        );
        ShowRowIf(group, "Pray Distance", () => runtimeSettings.prayToActivate);

        CreateSliderField(
            group,
            "Hand Open Speed",
            () => runtimeSettings.initializationSpeed,
            v => runtimeSettings.initializationSpeed = v,
            0f,
            1f,
            tooltip: "Playback speed of the hand-open animation. Lower values = slower animation."
        );
        CreateFloatField(
            group,
            "Re-arm Delay",
            () => runtimeSettings.initializationResetDelay,
            v => runtimeSettings.initializationResetDelay = v,
            tooltip: SceneController.ReArmDelayTooltip
        );
    }

    private void CreatePushGroup(SettingSection section)
    {
        var group = CreateGroup("Push", section);

        CreateFloatField(
            group,
            "Push Force",
            () => runtimeSettings.pushForce,
            v => runtimeSettings.pushForce = v,
            tooltip: "Base rigidbody force driving the ball toward the hand target (midpoint of both hands, or the single open hand). Everything else in Push multiplies it."
        );
        CreateCurveField(
            group,
            "Force To Middle",
            () => runtimeSettings.forceToMiddle,
            v => runtimeSettings.forceToMiddle = v,
            tooltip: "Curve of push-force strength vs. how close the ball is to its target. X: 0 = ball is Max Hand Spread away, 1 = ball is at the target. Y multiplies Push Force."
        );
        CreateFloatField(
            group,
            "Min Drag",
            () => runtimeSettings.minDrag,
            v => runtimeSettings.minDrag = v,
            tooltip: "Rigidbody linear damping when the ball is far from the hand target (at Max Hand Spread). Lower = ball keeps its momentum longer."
        );
        CreateFloatField(
            group,
            "Max Drag",
            () => runtimeSettings.maxDrag,
            v => runtimeSettings.maxDrag = v,
            tooltip: "Rigidbody linear damping when the ball is right at the hand target. Higher = ball settles quickly instead of overshooting."
        );
        CreateFloatField(
            group,
            "Final Push Scaler",
            () => runtimeSettings.handPushScaler,
            v => runtimeSettings.handPushScaler = v,
            tooltip: "Extra multiplier on the push force applied while both hands are closed (the final flick that sends the ball off). Drag is set to 0 during this push."
        );
        CreateFloatField(
            group,
            "Max Hand Spread",
            () => runtimeSettings.maxDistanceBetweenHands,
            v => runtimeSettings.maxDistanceBetweenHands = v,
            tooltip: "Reference hand separation used to normalize several curves (Force To Middle, Alignment Strength, Distance Damper) and the drag remap. Distances beyond it are clamped."
        );
    }

    private void CreateAimGroup(SettingSection section)
    {
        var group = CreateGroup("Aim", section);

        CreateCurveField(
            group,
            "Alignment Strength",
            () => runtimeSettings.alignmentVectorStrength,
            v => runtimeSettings.alignmentVectorStrength = v,
            tooltip: "Curve of how far the target is offset along the direction the hands point (wrist to fingertip). X: 0 = hands together, 1 = hands at Max Hand Spread. Y multiplies Alignment Scaler."
        );
        CreateFloatField(
            group,
            "Alignment Scaler",
            () => runtimeSettings.alignmentVectorStrengthScaler,
            v => runtimeSettings.alignmentVectorStrengthScaler = v,
            tooltip: "Max distance the hand target is pushed along the hands' pointing direction. Lets players aim the ball by tilting their hands rather than only by moving them."
        );
        CreateFloatField(
            group,
            "Torso Forward Offset",
            () => runtimeSettings.torsoMaxForwardOffset,
            v => runtimeSettings.torsoMaxForwardOffset = v,
            tooltip: "How far the ball's push target is pulled toward the camera when the hands sit at torso depth, so the ball isn't occluded by the player's own body. 0 disables."
        );
        CreateFloatField(
            group,
            "Torso Offset Falloff",
            () => runtimeSettings.torsoOffsetFalloffDistance,
            v => runtimeSettings.torsoOffsetFalloffDistance = v,
            tooltip: "Hand-to-torso z distance at which the torso forward offset fades to zero."
        );
    }

    private void CreateOneHandGroup(SettingSection section)
    {
        var group = CreateGroup("One Hand", section);

        CreateFloatField(
            group,
            "Open Force Damper",
            () => runtimeSettings.singleHandOpenForceDamper,
            v => runtimeSettings.singleHandOpenForceDamper = v,
            tooltip: "Multiplier on Push Force while only one hand is open (0-1). Lets one-handed steering be gentler than two-handed."
        );
        CreateFloatField(
            group,
            "Open Threshold",
            () => runtimeSettings.singleHandOpenThreshold,
            v => runtimeSettings.singleHandOpenThreshold = v,
            tooltip: "Minimum time in single-hand-open state before the final push uses that hand's position. Accounts for slight timing discrepancies with real Kinect users."
        );
        CreateFloatField(
            group,
            "Force Blend Time",
            () => runtimeSettings.singleHandForceLerpDuration,
            v => runtimeSettings.singleHandForceLerpDuration = v,
            tooltip: "Seconds to blend the push force from Open Force Damper back to full strength after the second hand opens."
        );
    }

    private void CreateGrowShrinkGroup(SettingSection section)
    {
        var group = CreateGroup("Grow & Shrink", section);

        CreateToggleField(
            group,
            "Single Hand Scaling",
            () => runtimeSettings.singleHandScaling,
            v => runtimeSettings.singleHandScaling = v,
            tooltip: "Allow scaling to occur with only one hand's velocity."
        );
        CreateCurveField(
            group,
            "Distance Damper",
            () => runtimeSettings.distanceDamper,
            v => runtimeSettings.distanceDamper = v,
            tooltip: "Curve scaling the grow/shrink effect by hand separation. X: 0 = hands at Max Hand Spread, 1 = hands together. Y multiplies the scale change, so hands close to the ball have more effect."
        );
        CreateFloatField(
            group,
            "Strength",
            () => runtimeSettings.pulseScaleDamper,
            v => runtimeSettings.pulseScaleDamper = v,
            tooltip: "An overall multiplier on how much hand movement grows or shrinks the ball."
        );
        CreateSliderField(
            group,
            "Min Hand Displacement",
            () => runtimeSettings.minHandDisplacementPerFrame,
            v => runtimeSettings.minHandDisplacementPerFrame = v,
            0.0001f,
            5f,
            tooltip: "Per-frame hand movement below this is ignored, masking false velocity readings from sensor position jitter."
        );
        CreateFloatField(
            group,
            "Max Hand Velocity",
            () => runtimeSettings.maxHandVelocity,
            v => runtimeSettings.maxHandVelocity = v,
            tooltip: "Hand-velocity sanity gate: frames where a hand moves faster than this are ignored as tracking glitches."
        );
    }

    // ---- Particles (the hand VFX: HandVfxSettings, nested in the scene profile as "handVfx") ----
    // Every dimensioned row shows the base value; the effective value is base × bodyScale^exp.

    private void CreateParticleColorGroup(SettingSection section)
    {
        var group = CreateGroup("Color", section);

        CreateToggleField(
            group,
            "Color Per Player",
            () => runtimeSettings.individualColors,
            v => runtimeSettings.individualColors = v,
            tooltip: "Give each new player its own slot in the Particle Palette (and the Skeleton Palette) instead of the shared Particle Gradient."
        );
        CreateGradientField(
            group,
            "Particle Gradient",
            () => runtimeSettings.particleColor,
            v => runtimeSettings.particleColor = v,
            hdr: true,
            tooltip: "Hand particle gradient for every player while Color Per Player is off."
        );
        ShowRowIf(group, "Particle Gradient", () => !runtimeSettings.individualColors);

        CreateGradientArrayField(
            group,
            "Particle Palette",
            () => runtimeSettings.particleColors,
            v => runtimeSettings.particleColors = v,
            hdr: true,
            tooltip: "The Color Per Player palette: one particle gradient per slot. Its length is the number of slots; each new player takes a free slot (random among them)."
        );
        ShowRowIf(group, "Particle Palette", () => runtimeSettings.individualColors);

        CreateFloatField(
            group,
            "Color Cycle Time (s)",
            () => runtimeSettings.handVfx.colorCycleTime,
            v => runtimeSettings.handVfx.colorCycleTime = v,
            tooltip: "Seconds for a particle to travel through the player's gradient (sampled by age, then held on the last color)."
        );
    }

    private void CreateGlowGroup(SettingSection section)
    {
        var group = CreateGroup("Glow", section);

        CreateFloatField(
            group,
            "Full Radius (radii)",
            () => runtimeSettings.handVfx.glowFullRadius,
            v => runtimeSettings.handVfx.glowFullRadius = v,
            tooltip: "Distance from the ball's (vfxSphere's) centre, in its radii, at which the surface glow is full (1 = surface)."
        );
        CreateFloatField(
            group,
            "Range (radii)",
            () => runtimeSettings.handVfx.glowRange,
            v => runtimeSettings.handVfx.glowRange = v,
            tooltip: "How far outside Full Radius the glow starts ramping in, in vfxSphere radii."
        );
        CreateCurveField(
            group,
            "Curve",
            () => runtimeSettings.handVfx.glowCurve,
            v => runtimeSettings.handVfx.glowCurve = v,
            tooltip: "Glow amount across the ramp. X 0 = Range out, X 1 = Full Radius. Y = glow (0-1)."
        );
        CreateFloatField(
            group,
            "Surface Brightness",
            () => runtimeSettings.handVfx.surfaceBrightness,
            v => runtimeSettings.handVfx.surfaceBrightness = v
        );
    }

    private void CreateEmissionGroup(SettingSection section)
    {
        var group = CreateGroup("Emission", section);

        CreateIntField(
            group,
            "Spawn Rate",
            () => runtimeSettings.handVfx.spawnRate,
            v => runtimeSettings.handVfx.spawnRate = v
        );
        CreateFloatField(
            group,
            "Spawn Radius",
            () => runtimeSettings.handVfx.spawnSphereRadius,
            v => runtimeSettings.handVfx.spawnSphereRadius = v
        );
        CreateFloatField(
            group,
            "Velocity Spread",
            () => runtimeSettings.handVfx.spawnVeloSpread,
            v => runtimeSettings.handVfx.spawnVeloSpread = v
        );
        CreateVector2Field(
            group,
            "Size Range",
            () => runtimeSettings.handVfx.sizeRange,
            v => runtimeSettings.handVfx.sizeRange = v
        );
    }

    private void CreateLifetimeGroup(SettingSection section)
    {
        var group = CreateGroup("Lifetime", section);

        CreateFloatField(
            group,
            "Max Lifetime (s)",
            () => runtimeSettings.handVfx.maxLifetime,
            v => runtimeSettings.handVfx.maxLifetime = v
        );
        CreateFloatField(
            group,
            "Max Lifetime Closed (s)",
            () => runtimeSettings.handVfx.maxLifetimeClosed,
            v => runtimeSettings.handVfx.maxLifetimeClosed = v
        );
        CreateFloatField(
            group,
            "Fade In Time (s)",
            () => runtimeSettings.handVfx.fadeInTime,
            v => runtimeSettings.handVfx.fadeInTime = v
        );
        CreateCurveField(
            group,
            "Fade In Curve",
            () => runtimeSettings.handVfx.fadeInCurve,
            v => runtimeSettings.handVfx.fadeInCurve = v,
            tooltip: "Size multiplier over the fade-in. X 0 = spawn, X 1 = Fade In Time. Y = fraction of full size."
        );
        CreateFloatField(
            group,
            "Fade Out Time (s)",
            () => runtimeSettings.handVfx.fadeOutTime,
            v => runtimeSettings.handVfx.fadeOutTime = v
        );
    }

    private void CreateStretchGroup(SettingSection section)
    {
        var group = CreateGroup("Stretch", section);

        CreateFloatField(
            group,
            "Length Scaler",
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

    private void CreateBallAttractionGroup(SettingSection section)
    {
        var group = CreateGroup("Ball Attraction", section);

        CreateFloatField(
            group,
            "Attraction Speed",
            () => runtimeSettings.handVfx.mainAttractionSpeed,
            v => runtimeSettings.handVfx.mainAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "Attraction Force",
            () => runtimeSettings.handVfx.mainAttractionForce,
            v => runtimeSettings.handVfx.mainAttractionForce = v
        );
        CreateFloatField(
            group,
            "Stick Distance",
            () => runtimeSettings.handVfx.mainStickDistance,
            v => runtimeSettings.handVfx.mainStickDistance = v
        );
        CreateFloatField(
            group,
            "Stick Force",
            () => runtimeSettings.handVfx.mainStickForce,
            v => runtimeSettings.handVfx.mainStickForce = v
        );
        CreateFloatField(
            group,
            "Seek Strength",
            () => runtimeSettings.handVfx.seekStrength,
            v => runtimeSettings.handVfx.seekStrength = v
        );
        CreateFloatField(
            group,
            "Tangential Damping (1/s)",
            () => runtimeSettings.handVfx.tangentialDamping,
            v => runtimeSettings.handVfx.tangentialDamping = v
        );
        CreateVector2Field(
            group,
            "Tangential Damping Fade",
            () => runtimeSettings.handVfx.tangentialDampingFade,
            v => runtimeSettings.handVfx.tangentialDampingFade = v,
            tooltip: "Distance range over which tangential damping fades in, as multiples of the vfxSphere's size (1 = its surface). X = start (no damping closer than this), Y = end (full damping beyond)."
        );
        CreateCurveField(
            group,
            "Tangential Damping Fade Curve",
            () => runtimeSettings.handVfx.tangentialDampingFadeCurve,
            v => runtimeSettings.handVfx.tangentialDampingFadeCurve = v,
            tooltip: "Damping weight across the fade range. X 0 = fade start, X 1 = fade end (remapped, so the shape is independent of the distances). Y = fraction of tangential damping applied."
        );
        CreateFloatField(
            group,
            "Collision Shape Scale",
            () => runtimeSettings.handVfx.vfxSphereCollisionScaleMult,
            v => runtimeSettings.handVfx.vfxSphereCollisionScaleMult = v,
            tooltip: "Size of the SDF collision shape relative to the ball (vfxSphere)."
        );
        CreateFloatField(
            group,
            "Collision Trigger Scale",
            () => runtimeSettings.handVfx.collisionDetectionScaleMult,
            v => runtimeSettings.handVfx.collisionDetectionScaleMult = v,
            tooltip: "Size of the collision-trigger sphere relative to the ball (vfxSphere)."
        );
    }

    private void CreateSecondaryAttractorGroup(SettingSection section)
    {
        var group = CreateGroup("Secondary Attractor", section);

        CreateFloatField(
            group,
            "Attraction Speed",
            () => runtimeSettings.handVfx.saAttractionSpeed,
            v => runtimeSettings.handVfx.saAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "Attraction Force",
            () => runtimeSettings.handVfx.saAttractionForce,
            v => runtimeSettings.handVfx.saAttractionForce = v
        );
        CreateFloatField(
            group,
            "Stick Distance",
            () => runtimeSettings.handVfx.saStickDistance,
            v => runtimeSettings.handVfx.saStickDistance = v
        );
        CreateFloatField(
            group,
            "Stick Force",
            () => runtimeSettings.handVfx.saStickForce,
            v => runtimeSettings.handVfx.saStickForce = v
        );
        CreateFloatField(
            group,
            "Min Radius",
            () => runtimeSettings.handVfx.saMinRadius,
            v => runtimeSettings.handVfx.saMinRadius = v
        );
    }

    private void CreateTrailDistortersGroup(SettingSection section)
    {
        var group = CreateGroup("Trail Distorters", section);

        CreateFloatField(
            group,
            "Radius",
            () => runtimeSettings.handVfx.tdRadius,
            v => runtimeSettings.handVfx.tdRadius = v
        );
        CreateFloatField(
            group,
            "Attraction Speed",
            () => runtimeSettings.handVfx.tdAttractionSpeed,
            v => runtimeSettings.handVfx.tdAttractionSpeed = v
        );
        CreateFloatField(
            group,
            "Attraction Force",
            () => runtimeSettings.handVfx.tdAttractionForce,
            v => runtimeSettings.handVfx.tdAttractionForce = v
        );
        CreateFloatField(
            group,
            "Stick Distance",
            () => runtimeSettings.handVfx.tdStickDistance,
            v => runtimeSettings.handVfx.tdStickDistance = v
        );
        CreateFloatField(
            group,
            "Stick Force",
            () => runtimeSettings.handVfx.tdStickForce,
            v => runtimeSettings.handVfx.tdStickForce = v
        );
        CreateFloatField(
            group,
            "Wander Amount",
            () => runtimeSettings.handVfx.tdWanderAmount,
            v => runtimeSettings.handVfx.tdWanderAmount = v
        );
    }

    private void CreateNoiseGroup(SettingSection section)
    {
        var group = CreateGroup("Noise & Turbulence", section);

        CreateFloatField(
            group,
            "Noise Scale",
            () => runtimeSettings.handVfx.noiseScale,
            v => runtimeSettings.handVfx.noiseScale = v
        );
        CreateFloatField(
            group,
            "Noise Frequency",
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
            "Turbulence Intensity",
            () => runtimeSettings.handVfx.turbulenceIntensity,
            v => runtimeSettings.handVfx.turbulenceIntensity = v
        );
        CreateFloatField(
            group,
            "Turbulence Frequency",
            () => runtimeSettings.handVfx.turbulenceFrequency,
            v => runtimeSettings.handVfx.turbulenceFrequency = v
        );
    }

    private void CreateHiHatBurstsGroup(SettingSection section)
    {
        var group = CreateGroup("Hi-Hat Bursts", section);

        CreateFloatField(
            group,
            "Closed Size",
            () => runtimeSettings.handVfx.cHatSize,
            v => runtimeSettings.handVfx.cHatSize = v,
            tooltip: "Particle size of the closed hi-hat burst."
        );
        CreateFloatField(
            group,
            "Closed Noise Amp",
            () => runtimeSettings.handVfx.cHatNoiseAmp,
            v => runtimeSettings.handVfx.cHatNoiseAmp = v
        );
        CreateFloatField(
            group,
            "Closed Noise Freq",
            () => runtimeSettings.handVfx.cHatNoiseFreq,
            v => runtimeSettings.handVfx.cHatNoiseFreq = v
        );
        CreateFloatField(
            group,
            "Closed Noise Y Scroll",
            () => runtimeSettings.handVfx.cHatNoiseYScroll,
            v => runtimeSettings.handVfx.cHatNoiseYScroll = v
        );
        CreateFloatField(
            group,
            "Closed Spawn Velocity",
            () => runtimeSettings.handVfx.cHatSpawnVeloSphereRadius,
            v => runtimeSettings.handVfx.cHatSpawnVeloSphereRadius = v,
            tooltip: "Radius of the random spawn-velocity sphere of the closed hi-hat burst."
        );
        CreateFloatField(
            group,
            "Open Size",
            () => runtimeSettings.handVfx.oHatSize,
            v => runtimeSettings.handVfx.oHatSize = v,
            tooltip: "Particle size of the open hi-hat burst."
        );
        CreateFloatField(
            group,
            "Open Noise Amp",
            () => runtimeSettings.handVfx.oHatNoiseAmp,
            v => runtimeSettings.handVfx.oHatNoiseAmp = v
        );
        CreateFloatField(
            group,
            "Open Noise Freq",
            () => runtimeSettings.handVfx.oHatNoiseFreq,
            v => runtimeSettings.handVfx.oHatNoiseFreq = v
        );
        CreateFloatField(
            group,
            "Open Noise Y Scroll",
            () => runtimeSettings.handVfx.oHatNoiseYScroll,
            v => runtimeSettings.handVfx.oHatNoiseYScroll = v
        );
    }

    private void CreateSnareBurstsGroup(SettingSection section)
    {
        var group = CreateGroup("Snare Bursts", section);

        CreateVector2Field(
            group,
            "Size Range",
            () => runtimeSettings.handVfx.snareSizeRange,
            v => runtimeSettings.handVfx.snareSizeRange = v
        );
        CreateVector2Field(
            group,
            "Radius Range",
            () => runtimeSettings.handVfx.snareRadiusRandRange,
            v => runtimeSettings.handVfx.snareRadiusRandRange = v,
            tooltip: "Random range of the snare burst's spawn-circle radius."
        );
        CreateFloatField(
            group,
            "Spawn Velocity",
            () => runtimeSettings.handVfx.snareSpawnVeloSphereRadius,
            v => runtimeSettings.handVfx.snareSpawnVeloSphereRadius = v,
            tooltip: "Radius of the random spawn-velocity sphere of the snare burst."
        );
    }

    // ---- Debug ----

    private void CreateVisualizersGroup(SettingSection section)
    {
        var group = CreateGroup("Visualizers", section);

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

    private void CreatePostProcessingGroup(ScrollView parentContainer)
    {
        var bloomGroup = CreateGroup("Bloom", parentContainer);

        CreateFloatField(
            bloomGroup,
            "Bloom Threshold",
            () => ActivePP.bloomThreshold,
            v => ActivePP.bloomThreshold = v
        );
        CreateSliderField(
            bloomGroup,
            "Bloom Intensity",
            () => ActivePP.bloomIntensity,
            v => ActivePP.bloomIntensity = v,
            0f,
            3f
        );
        CreateSliderField(
            bloomGroup,
            "Bloom Scatter",
            () => ActivePP.bloomScatter,
            v => ActivePP.bloomScatter = v,
            0f,
            1f
        );

        var lensFlareGroup = CreateGroup("Screen Space Lens Flare", parentContainer);

        CreateSliderField(
            lensFlareGroup,
            "Intensity",
            () => ActivePP.lensFlareIntensity,
            v => ActivePP.lensFlareIntensity = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Regular Multiplier (Flares)",
            () => ActivePP.lensFlareRegularMultiplier,
            v => ActivePP.lensFlareRegularMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Reversed Multiplier (Flares)",
            () => ActivePP.lensFlareReversedMultiplier,
            v => ActivePP.lensFlareReversedMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Multiplier (Streaks)",
            () => ActivePP.lensFlareStreaksMultiplier,
            v => ActivePP.lensFlareStreaksMultiplier = v,
            0f,
            3f
        );
        CreateSliderField(
            lensFlareGroup,
            "Length (Streaks)",
            () => ActivePP.lensFlareStreaksLength,
            v => ActivePP.lensFlareStreaksLength = v,
            0f,
            1f
        );
        CreateSliderField(
            lensFlareGroup,
            "Orientation (Streaks)",
            () => ActivePP.lensFlareStreaksOrientation,
            v => ActivePP.lensFlareStreaksOrientation = v,
            -180f,
            180f
        );
        CreateSliderField(
            lensFlareGroup,
            "Threshold (Streaks)",
            () => ActivePP.lensFlareStreaksThreshold,
            v => ActivePP.lensFlareStreaksThreshold = v,
            0f,
            1f
        );
        CreateSliderField(
            lensFlareGroup,
            "Chromatic Aberration Intensity",
            () => ActivePP.lensFlareChromaticIntensity,
            v => ActivePP.lensFlareChromaticIntensity = v,
            0f,
            1f
        );

        var lensDistortionGroup = CreateGroup("Lens Distortion", parentContainer);

        CreateSliderField(
            lensDistortionGroup,
            "Intensity",
            () => ActivePP.lensDistortionIntensity,
            v => ActivePP.lensDistortionIntensity = v,
            -1f,
            1f
        );
        CreateSliderField(
            lensDistortionGroup,
            "X Multiplier",
            () => ActivePP.lensDistortionXMultiplier,
            v => ActivePP.lensDistortionXMultiplier = v,
            0f,
            2f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Y Multiplier",
            () => ActivePP.lensDistortionYMultiplier,
            v => ActivePP.lensDistortionYMultiplier = v,
            0f,
            2f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Scale",
            () => ActivePP.lensDistortionScale,
            v => ActivePP.lensDistortionScale = v,
            0.01f,
            3f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Center X",
            () => ActivePP.lensDistortionCenterX,
            v => ActivePP.lensDistortionCenterX = v,
            0f,
            1f
        );
        CreateSliderField(
            lensDistortionGroup,
            "Center Y",
            () => ActivePP.lensDistortionCenterY,
            v => ActivePP.lensDistortionCenterY = v,
            0f,
            1f
        );

        var colorAdjustmentsGroup = CreateGroup("Color Adjustments", parentContainer);

        CreateFloatField(
            colorAdjustmentsGroup,
            "Post Exposure",
            () => ActivePP.colorAdjustmentsPostExposure,
            v => ActivePP.colorAdjustmentsPostExposure = v
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Contrast",
            () => ActivePP.colorAdjustmentsContrast,
            v => ActivePP.colorAdjustmentsContrast = v,
            -100f,
            100f
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Hue Shift",
            () => ActivePP.colorAdjustmentsHueShift,
            v => ActivePP.colorAdjustmentsHueShift = v,
            -180f,
            180f
        );
        CreateSliderField(
            colorAdjustmentsGroup,
            "Saturation",
            () => ActivePP.colorAdjustmentsSaturation,
            v => ActivePP.colorAdjustmentsSaturation = v,
            -100f,
            100f
        );

        var whiteBalanceGroup = CreateGroup("White Balance", parentContainer);

        CreateSliderField(
            whiteBalanceGroup,
            "Temperature",
            () => ActivePP.whiteBalanceTemperature,
            v => ActivePP.whiteBalanceTemperature = v,
            -100f,
            100f
        );
        CreateSliderField(
            whiteBalanceGroup,
            "Tint",
            () => ActivePP.whiteBalanceTint,
            v => ActivePP.whiteBalanceTint = v,
            -100f,
            100f
        );
    }

    /// <summary>
    /// Records <paramref name="row"/> as the row labelled <paramref name="label"/> in
    /// <paramref name="group"/>, so <see cref="ShowRowIf"/> can find it. Labels only need to be
    /// unique within a group.
    /// </summary>
    private void RegisterRow(VisualElement group, string label, VisualElement row)
    {
        if (!settingRows.TryAdd((group, label), row))
            Debug.LogWarning($"Settings menu: two rows labelled '{label}' in one group.");
    }

    /// <summary>
    /// Shows the row labelled <paramref name="label"/> in <paramref name="group"/> only while
    /// <paramref name="isVisible"/> holds. Call right after creating the field.
    /// </summary>
    private void ShowRowIf(VisualElement group, string label, Func<bool> isVisible)
    {
        if (!settingRows.TryGetValue((group, label), out var row))
        {
            Debug.LogWarning($"Settings menu: ShowRowIf found no row labelled '{label}'.");
            return;
        }
        conditionalRows.Add((row, isVisible));
        row.EnableInClassList("condition-hidden", !isVisible());
    }

    private void UpdateConditionalRows()
    {
        foreach (var (row, isVisible) in conditionalRows)
            row.EnableInClassList("condition-hidden", !isVisible());

        // A row appearing or disappearing can change which groups have search matches.
        if (conditionalRows.Count > 0)
            ApplyAllSearches();
    }

    /// <summary>
    /// Adds a section (a titled page of groups, e.g. "Ball") to <paramref name="panel"/> and its
    /// entry to the sidebar <paramref name="nav"/>. Which page shows is applied by
    /// <see cref="ApplySearch"/>.
    /// </summary>
    private SettingSection CreateSection(string title, ScrollView panel, VisualElement nav)
    {
        var root = new VisualElement();
        root.AddToClassList("settings-section");

        var titleLabel = new Label(title);
        titleLabel.AddToClassList("section-title");
        root.Add(titleLabel);

        var content = new VisualElement();
        content.AddToClassList("section-content");
        root.Add(content);

        panel.Add(root);

        var section = new SettingSection
        {
            key = panel.name + "/" + title,
            title = title,
            panel = panel,
            root = root,
            content = content,
        };
        settingSections.Add(section);

        if (nav != null)
        {
            section.navItem = new Button(() => SelectSection(section)) { text = title };
            section.navItem.AddToClassList("section-nav-item");
            nav.Add(section.navItem);
        }

        return section;
    }

    private void SelectSection(SettingSection section)
    {
        // While searching every page with matches is listed, so jump to this one instead.
        if (IsSearching(section.panel))
        {
            if (!section.root.ClassListContains("search-hidden"))
                section.panel.ScrollTo(section.root);
            return;
        }

        selectedSection = section.title;
        PlayerPrefs.SetString(SelectedSectionPrefKey, selectedSection);
        ApplySectionSelection(section.panel, searching: false);
        section.panel.scrollOffset = Vector2.zero;
    }

    /// <summary>
    /// Shows the selected page of <paramref name="panel"/> (the first when the selected one isn't
    /// built, e.g. Kinect in dummy-only mode) and marks its sidebar entry. While searching, the
    /// search decides which pages show.
    /// </summary>
    private void ApplySectionSelection(ScrollView panel, bool searching)
    {
        var sections = settingSections.FindAll(s => s.panel == panel);
        if (sections.Count == 0)
            return;

        var selected = sections.Find(s => s.title == selectedSection) ?? sections[0];
        foreach (var section in sections)
        {
            section.root.EnableInClassList("section-inactive", !searching && section != selected);
            section.navItem?.EnableInClassList("active", !searching && section == selected);
        }
    }

    /// <summary>Adds a collapsible group to <paramref name="section"/>; see the panel overload.</summary>
    private VisualElement CreateGroup(string title, SettingSection section) =>
        CreateGroup(title, section.panel, section);

    /// <summary>
    /// Adds a collapsible group to <paramref name="panel"/> (inside <paramref name="section"/>
    /// when given) and returns the element its setting rows go into. Clicking the header
    /// collapses / expands it.
    /// </summary>
    private VisualElement CreateGroup(string title, ScrollView panel, SettingSection section = null)
    {
        var group = new VisualElement();
        group.AddToClassList("settings-group");

        var header = new VisualElement();
        header.AddToClassList("group-header");
        // Points down while expanded; the stylesheet rotates it by the collapsed class.
        var chevron = new Label("▶");
        chevron.AddToClassList("group-chevron");
        // A small all-caps caption over the group's card (USS has no text-transform).
        var titleLabel = new Label(title.ToUpperInvariant());
        titleLabel.AddToClassList("group-title");
        header.Add(chevron);
        header.Add(titleLabel);
        group.Add(header);

        var content = new VisualElement();
        content.AddToClassList("group-content");
        group.Add(content);

        (section != null ? section.content : panel).Add(group);

        var entry = new SettingGroup
        {
            key = (section != null ? section.key : panel.name) + "/" + title,
            title = title,
            panel = panel,
            section = section,
            root = group,
            content = content,
        };
        settingGroups.Add(entry);
        header.RegisterCallback<ClickEvent>(_ => ToggleGroupCollapsed(entry));
        ApplyGroupCollapsed(entry, searching: false);

        return content;
    }

    private void ToggleGroupCollapsed(SettingGroup group)
    {
        // While searching every group with matches is forced open, so collapsing does nothing.
        if (IsSearching(group.panel))
            return;

        ToggleCollapsedKey(group.key);
        ApplyGroupCollapsed(group, searching: false);
    }

    private void ToggleCollapsedKey(string key)
    {
        if (!collapsedGroups.Remove(key))
            collapsedGroups.Add(key);
        PlayerPrefs.SetString(CollapsedGroupsPrefKey, string.Join("\n", collapsedGroups));
    }

    private void ApplyGroupCollapsed(SettingGroup group, bool searching)
    {
        bool collapsed = !searching && collapsedGroups.Contains(group.key);
        group.root.EnableInClassList("collapsed", collapsed);
    }

    // ---- Search ----

    private void SetupSearchField(TextField field, ScrollView panel)
    {
        if (field == null || panel == null)
            return;

        field.textEdition.placeholder = "Search settings...";
        field.RegisterValueChangedCallback(_ =>
        {
            ApplySearch(panel);
            panel.scrollOffset = Vector2.zero;
        });
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.keyCode == KeyCode.Escape)
                field.value = "";
        });
    }

    private TextField SearchFieldFor(ScrollView panel) =>
        panel == sceneSettingsPanel ? sceneSearchField
        : panel == postProcessingPanel ? postProcessingSearchField
        : null;

    private string[] SearchTerms(ScrollView panel) =>
        (SearchFieldFor(panel)?.value ?? "")
            .ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private bool IsSearching(ScrollView panel) => SearchTerms(panel).Length > 0;

    private static bool MatchesAllTerms(string text, string[] terms)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        text = text.ToLowerInvariant();
        foreach (var term in terms)
            if (!text.Contains(term))
                return false;
        return true;
    }

    private void ApplyAllSearches()
    {
        ApplySearch(sceneSettingsPanel);
        ApplySearch(postProcessingPanel);
    }

    /// <summary>
    /// Filters <paramref name="panel"/> to the rows whose section title + group title + label
    /// contain every search term, so a matching section or group title shows all its rows, and
    /// rows sharing a label are told apart by where they live ("trail stick force"). Every page
    /// with matches is listed and its groups expanded; clearing the search restores the selected
    /// page and the collapsed groups.
    /// </summary>
    private void ApplySearch(ScrollView panel)
    {
        if (panel == null)
            return;

        var terms = SearchTerms(panel);
        bool searching = terms.Length > 0;
        bool anyMatch = false;
        var sectionsWithMatches = new HashSet<SettingSection>();

        foreach (var group in settingGroups)
        {
            if (group.panel != panel)
                continue;

            string context =
                (group.section != null ? group.section.title + " " : "") + group.title + " ";
            bool groupHasMatch = false;
            foreach (var row in group.content.Children())
            {
                // Terms can come from the titles and the label together ("lens scale").
                bool show =
                    !searching
                    || MatchesAllTerms(
                        context + row.Q<Label>(className: "setting-label")?.text,
                        terms
                    );
                row.EnableInClassList("search-hidden", !show);

                // Rows are divided by a top border, which the first visible one drops
                // (USS has no :first-child).
                bool visible = show && !row.ClassListContains("condition-hidden");
                row.EnableInClassList("row-first", visible && !groupHasMatch);
                groupHasMatch |= visible;
            }

            bool showGroup = !searching || groupHasMatch;
            group.root.EnableInClassList("search-hidden", !showGroup);
            ApplyGroupCollapsed(group, searching);
            anyMatch |= showGroup;
            if (showGroup && group.section != null)
                sectionsWithMatches.Add(group.section);
        }

        foreach (var section in settingSections)
        {
            if (section.panel != panel)
                continue;
            bool noMatch = searching && !sectionsWithMatches.Contains(section);
            section.root.EnableInClassList("search-hidden", noMatch);
            section.navItem?.EnableInClassList("no-match", noMatch);
        }
        ApplySectionSelection(panel, searching);

        if (searchEmptyLabels.TryGetValue(panel, out var emptyLabel))
            emptyLabel.EnableInClassList("hidden", !searching || anyMatch);
    }

    private void AddSearchEmptyLabel(ScrollView panel)
    {
        var label = new Label("No matching settings");
        label.AddToClassList("search-empty-label");
        label.AddToClassList("hidden");
        panel.Add(label);
        searchEmptyLabels[panel] = label;
    }

    /// <summary>
    /// True while a text-entry field (search, number fields, ...) has keyboard focus, so typing
    /// doesn't trigger the menu's hotkey.
    /// </summary>
    private bool IsTextFieldFocused()
    {
        var focused = uiDocument?.rootVisualElement?.panel?.focusController?.focusedElement;
        for (var element = focused as VisualElement; element != null; element = element.parent)
            if (element.ClassListContains("unity-base-text-field"))
                return true;
        return false;
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

        RegisterRow(parent, label, row);
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

        RegisterRow(parent, label, row);
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

        RegisterRow(parent, label, row);
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

        RegisterRow(parent, label, row);
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

        var slider = new Slider(min, max) { fill = true };
        slider.style.flexGrow = 1;
        slider.value = getter();

        // A number box beside the slider, like the inspector's [Range] field: it follows the
        // slider, and a typed value (committed on Enter / blur) is clamped to the range.
        var valueField = new FloatField { isDelayed = true, value = getter() };
        valueField.name = label;
        valueField.AddToClassList("slider-value");

        // Dragging lands on about 1/500 of the range, so the box doesn't show float noise.
        int decimals = Mathf.Clamp(
            Mathf.CeilToInt(-Mathf.Log10(Mathf.Abs(max - min) / 500f)),
            0,
            6
        );

        slider.RegisterValueChangedCallback(evt =>
        {
            float value = (float)Math.Round(evt.newValue, decimals);
            valueField.SetValueWithoutNotify(value);
            setter(value);
            NotifySettingsChanged();
        });

        valueField.RegisterValueChangedCallback(evt =>
        {
            float value = Mathf.Clamp(evt.newValue, Mathf.Min(min, max), Mathf.Max(min, max));
            valueField.SetValueWithoutNotify(value);
            slider.SetValueWithoutNotify(value);
            setter(value);
            NotifySettingsChanged();
        });

        inputContainer.Add(slider);
        inputContainer.Add(valueField);

        row.Add(labelElement);
        row.Add(inputContainer);
        parent.Add(row);

        RegisterRow(parent, label, row);
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
        toggle.name = label;
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

        RegisterRow(parent, label, row);
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
            if (IsAnyPopupVisible)
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

        RegisterRow(parent, label, row);
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

    /// <summary>
    /// A color swatch row. Clicking the swatch opens the runtime color picker (HDR adds the
    /// Intensity control and allows values above 1).
    /// </summary>
    private void CreateColorField(
        VisualElement parent,
        string label,
        Func<Color> getter,
        Action<Color> setter,
        bool hdr = false,
        bool showAlpha = true,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var swatch = CreateColorSwatch(getter, setter, hdr, showAlpha);

        row.Add(labelElement);
        row.Add(swatch);
        parent.Add(row);

        RegisterRow(parent, label, row);
    }

    /// <summary>
    /// A gradient strip row. Clicking it opens the runtime gradient editor.
    /// </summary>
    private void CreateGradientField(
        VisualElement parent,
        string label,
        Func<Gradient> getter,
        Action<Gradient> setter,
        bool hdr = false,
        string tooltip = null
    )
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        AttachTooltip(labelElement, tooltip);

        var thumbnail = CreateGradientThumbnail(getter, setter, hdr);

        row.Add(labelElement);
        row.Add(thumbnail);
        parent.Add(row);

        RegisterRow(parent, label, row);
    }

    private VisualElement CreateColorSwatch(
        Func<Color> getter,
        Action<Color> setter,
        bool hdr,
        bool showAlpha
    )
    {
        var swatch = new VisualElement();
        swatch.AddToClassList("color-swatch");
        var hdrBadge = CreateHdrBadge(swatch);

        Color shown = default;
        bool hasRendered = false;
        AttachThumbnailTexture(
            swatch,
            (tex, resized) =>
            {
                Color color = getter();
                if (hasRendered && !resized && color == shown)
                    return;
                RuntimeColorThumbnails.RenderColorSwatch(tex, color, hdr, showAlpha);
                hdrBadge.style.display = hdr ? DisplayStyle.Flex : DisplayStyle.None; // like the editor: any HDR color
                shown = color;
                hasRendered = true;
            }
        );

        swatch.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (IsAnyPopupVisible)
                return;
            RuntimeColorPickerWindow.Show(
                getter(),
                hdr,
                showAlpha,
                color =>
                {
                    setter(color);
                    NotifySettingsChanged();
                }
            );
        });
        return swatch;
    }

    private VisualElement CreateGradientThumbnail(
        Func<Gradient> getter,
        Action<Gradient> setter,
        bool hdr
    )
    {
        var thumbnail = new VisualElement();
        thumbnail.AddToClassList("gradient-thumbnail");
        var hdrBadge = CreateHdrBadge(thumbnail);

        bool hasRendered = false;
        AttachThumbnailTexture(
            thumbnail,
            (tex, resized) =>
            {
                // Re-renders only while the gradient editor is open (or on resize); profile
                // loads rebuild the whole UI, so thumbnails get a fresh first render.
                if (hasRendered && !resized && !RuntimeGradientEditorWindow.IsVisible)
                    return;
                var gradient = getter();
                if (gradient == null)
                    return;
                RuntimeColorThumbnails.RenderGradient(tex, gradient);
                hdrBadge.style.display =
                    RuntimeGradientEditorWindow.MaxColorComponent(gradient) > 1f
                        ? DisplayStyle.Flex
                        : DisplayStyle.None;
                hasRendered = true;
            }
        );

        thumbnail.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (IsAnyPopupVisible)
                return;
            var gradient = getter();
            if (gradient == null)
                return;
            RuntimeGradientEditorWindow.Show(
                gradient,
                hdr,
                changed =>
                {
                    setter(changed);
                    NotifySettingsChanged();
                }
            );
        });
        return thumbnail;
    }

    private static Label CreateHdrBadge(VisualElement owner)
    {
        var badge = new Label("HDR");
        badge.AddToClassList("hdr-badge");
        badge.pickingMode = PickingMode.Ignore;
        badge.style.display = DisplayStyle.None;
        owner.Add(badge);
        return badge;
    }

    /// <summary>
    /// Gives <paramref name="element"/> a background texture sized to its layout and calls
    /// <paramref name="render"/> (texture, resized) every 200 ms while laid out. The texture is
    /// destroyed when the element leaves the panel (UI rebuild, array row removed).
    /// </summary>
    private static void AttachThumbnailTexture(
        VisualElement element,
        Action<Texture2D, bool> render
    )
    {
        Texture2D tex = null;
        element
            .schedule.Execute(() =>
            {
                int width = Mathf.RoundToInt(element.resolvedStyle.width);
                int height = Mathf.RoundToInt(element.resolvedStyle.height);
                if (width <= 4 || height <= 4)
                    return;

                bool resized = tex == null || tex.width != width || tex.height != height;
                if (resized)
                {
                    if (tex != null)
                        Destroy(tex);
                    tex = RuntimeColorThumbnails.CreateTexture(width, height);
                }
                render(tex, resized);
                element.style.backgroundImage = tex;
            })
            .Every(200);

        element.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            if (tex != null)
                Destroy(tex);
            tex = null;
        });
    }

    /// <summary>
    /// An array row with Add Element / remove buttons, like <see cref="CreateFloatArrayField"/>,
    /// where each element is edited by the control <paramref name="createElementEditor"/> builds
    /// from an element getter/setter.
    /// </summary>
    private void CreateArrayField<T>(
        VisualElement parent,
        string label,
        Func<T[]> getter,
        Action<T[]> setter,
        Func<Func<T>, Action<T>, VisualElement> createElementEditor,
        Func<T[], T> createNewElement,
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

        var headerContainer = new VisualElement();
        headerContainer.AddToClassList("array-header");

        var collapseButton = new Button { text = "⇑" };
        collapseButton.AddToClassList("array-collapse-button");

        var countLabel = new Label();
        countLabel.AddToClassList("array-count-label");

        var contentContainer = new VisualElement();
        contentContainer.AddToClassList("array-content");

        void Refresh()
        {
            var array = getter() ?? Array.Empty<T>();
            countLabel.text = $"({array.Length} items)";
            contentContainer.Clear();

            for (int i = 0; i < array.Length; i++)
            {
                int index = i; // Capture for closure
                var elementRow = new VisualElement();
                elementRow.AddToClassList("array-element");

                var editor = createElementEditor(
                    () =>
                    {
                        var current = getter();
                        return current != null && index < current.Length ? current[index] : default;
                    },
                    value =>
                    {
                        var current = getter();
                        if (current == null || index >= current.Length)
                            return;
                        current[index] = value;
                        setter(current);
                    }
                );
                editor.AddToClassList("array-element-input");

                var removeButton = new Button(() =>
                {
                    var current = new List<T>(getter() ?? Array.Empty<T>());
                    if (index >= current.Count)
                        return;
                    current.RemoveAt(index);
                    setter(current.ToArray());
                    Refresh();
                    NotifySettingsChanged();
                });
                removeButton.text = "-";
                removeButton.AddToClassList("array-button");

                elementRow.Add(editor);
                elementRow.Add(removeButton);
                contentContainer.Add(elementRow);
            }
        }

        var addButton = new Button(() =>
        {
            var current = getter() ?? Array.Empty<T>();
            var list = new List<T>(current) { createNewElement(current) };
            setter(list.ToArray());
            Refresh();
            NotifySettingsChanged();
        });
        addButton.text = "Add Element";
        addButton.AddToClassList("array-button");

        // Starts expanded: the elements are the point of these rows.
        bool isCollapsed = false;
        collapseButton.clicked += () =>
        {
            isCollapsed = !isCollapsed;
            collapseButton.text = isCollapsed ? "⇓" : "⇑";
            contentContainer.EnableInClassList("collapsed", isCollapsed);
            addButton.EnableInClassList("hidden", isCollapsed);
        };

        headerContainer.Add(collapseButton);
        headerContainer.Add(countLabel);
        headerContainer.Add(addButton);
        arrayContainer.Add(headerContainer);
        arrayContainer.Add(contentContainer);
        Refresh();

        var container = new VisualElement();
        container.AddToClassList("array-row-subcontainer");
        container.Add(labelElement);
        container.Add(arrayContainer);
        row.Add(container);
        parent.Add(row);

        RegisterRow(parent, label, row);
    }

    private void CreateColorArrayField(
        VisualElement parent,
        string label,
        Func<Color[]> getter,
        Action<Color[]> setter,
        bool hdr = false,
        bool showAlpha = true,
        string tooltip = null
    )
    {
        CreateArrayField(
            parent,
            label,
            getter,
            setter,
            (get, set) => CreateColorSwatch(get, set, hdr, showAlpha),
            current => current.Length > 0 ? current[^1] : Color.white,
            tooltip
        );
    }

    private void CreateGradientArrayField(
        VisualElement parent,
        string label,
        Func<Gradient[]> getter,
        Action<Gradient[]> setter,
        bool hdr = false,
        string tooltip = null
    )
    {
        CreateArrayField(
            parent,
            label,
            getter,
            setter,
            (get, set) => CreateGradientThumbnail(get, set, hdr),
            current =>
                current.Length > 0 ? ColorSettingsUtility.Clone(current[^1]) : new Gradient(),
            tooltip
        );
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

        RegisterRow(parent, label, row);
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
        settingsPanel.panel?.focusController?.focusedElement?.Blur();

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

        // A restored working set already knows both targets' profiles - just show the active one.
        if (!restoredFromWorkingSet)
        {
            // Particles: last used for this scene, else the first profile. The feed: its own
            // last used profile, else the same profile as the particles.
            AutoLoadPostProcessingProfile(
                PostProcessingTarget.Particles,
                profileFiles,
                profileFiles.FirstOrDefault()
            );
            AutoLoadPostProcessingProfile(
                PostProcessingTarget.Feed,
                profileFiles,
                particlePP.ProfileName
            );
        }

        string name = ActivePPState.ProfileName;
        if (!string.IsNullOrEmpty(name) && profileFiles.Contains(name))
            postProcessingProfileDropdown.SetValueWithoutNotify(name);
    }

    private void AutoLoadPostProcessingProfile(
        PostProcessingTarget target,
        List<string> profileFiles,
        string fallbackProfile
    )
    {
        string lastUsedProfile = PlayerPrefs.GetString(GetPPState(target).lastUsedKey, "");
        string profile = profileFiles.Contains(lastUsedProfile) ? lastUsedProfile : fallbackProfile;
        if (string.IsNullOrEmpty(profile) || !profileFiles.Contains(profile))
            return;
        LoadProfile(
            Path.Combine(postProcessingProfilesDirectory, profile + ".json"),
            ProfileType.PostProcessing,
            target
        );
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
            LoadProfile(profilePath, ProfileType.PostProcessing, activePostProcessingTarget);
        }
    }

    private void LoadProfile(
        string path,
        ProfileType profileType,
        PostProcessingTarget ppTarget = PostProcessingTarget.Particles
    )
    {
        if (!File.Exists(path))
            return;

        try
        {
            var json = File.ReadAllText(path);

            // Merge loaded settings based on profile type
            if (profileType == ProfileType.Scene)
            {
                var loadedSettings = JsonUtility.FromJson<RuntimeSceneSettings>(json);

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
                // Load the look into one target only; scene settings and the other target stay.
                // PP files are PostProcessSettings JSON (older ones are full settings dumps
                // whose flat PP keys map onto the same field names).
                var state = GetPPState(ppTarget);
                var loaded = JsonUtility.FromJson<PostProcessSettings>(json);
                state.set(runtimeSettings, loaded);
                state.profilePath = path;
                state.baselineJson = loaded.ToCanonicalJson();

                // Save as last used post-processing profile for this target
                string profileName = Path.GetFileNameWithoutExtension(path);
                PlayerPrefs.SetString(state.lastUsedKey, profileName);
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

        // Hand VFX (nested, copied as one object; old files without the key get C# defaults)
        runtimeSettings.handVfx =
            loadedSettings.handVfx != null
                ? loadedSettings.handVfx.DeepCopy()
                : new HandVfxSettings();

        // Activation, one-hand timing, ball spawn
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

        // Camera feed, skeleton and colors
        runtimeSettings.showCameraFeed = loadedSettings.showCameraFeed;
        runtimeSettings.individualColors = loadedSettings.individualColors;
        runtimeSettings.drawSkeleton = loadedSettings.drawSkeleton;
        runtimeSettings.useTrackingStateColors = loadedSettings.useTrackingStateColors;
        // Older profiles carry no colors; then the current ones stay.
        runtimeSettings.CopyStyleColorsFrom(loadedSettings);

        // Debug settings
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

        // Hand VFX (nested, copied as one object)
        destination.handVfx =
            source.handVfx != null ? source.handVfx.DeepCopy() : new HandVfxSettings();

        // Activation, one-hand timing, ball spawn
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

        // Camera feed, skeleton and colors
        destination.showCameraFeed = source.showCameraFeed;
        destination.individualColors = source.individualColors;
        destination.drawSkeleton = source.drawSkeleton;
        destination.useTrackingStateColors = source.useTrackingStateColors;
        destination.CopyStyleColorsFrom(source);

        // Debug settings
        destination.showSphereMeshOnHandCollision = source.showSphereMeshOnHandCollision;
        destination.alwaysShowSphereMesh = source.alwaysShowSphereMesh;
        destination.showMetaballMesh = source.showMetaballMesh;
        destination.showPointCloud = source.showPointCloud;
        destination.showMetaballBounds = source.showMetaballBounds;
        destination.showAttractionRadius = source.showAttractionRadius;
        destination.showHandTrailDistorters = source.showHandTrailDistorters;
        destination.showSecondaryAttractor = source.showSecondaryAttractor;
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
            if (string.IsNullOrEmpty(ActivePPState.profilePath))
            {
                ShowSaveAsDialog(tabType);
                return;
            }
            SaveProfile(ActivePPState.profilePath, tabType);
        }
    }

    private void ShowSaveAsDialog(TabType tabType)
    {
        isModalOpen = true;

        // Create modal dialog for save as
        var modal = new VisualElement();
        modal.AddToClassList("modal-overlay");

        var panel = new VisualElement();
        panel.AddToClassList("modal-panel");
        panel.style.width = 400;

        var title = new Label("Save Profile As...");
        title.AddToClassList("modal-title");
        panel.Add(title);

        var nameField = new TextField("Profile Name");
        nameField.AddToClassList("modal-field");
        nameField.value = $"Profile_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        nameField.SelectAll();
        panel.Add(nameField);

        var buttonContainer = new VisualElement();
        buttonContainer.AddToClassList("modal-buttons");

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
        saveButton.AddToClassList("modal-button");
        saveButton.AddToClassList("primary");

        var cancelButton = new Button(() =>
        {
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        cancelButton.text = "Cancel";
        cancelButton.AddToClassList("modal-button");

        // Platform order: dismiss on the left, the action on the right.
        buttonContainer.Add(cancelButton);
        buttonContainer.Add(saveButton);
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
            lastUsedKey = ActivePPState.lastUsedKey;
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
            string json;

            if (tabType == TabType.Scene)
            {
                // Create a clean settings object with only scene-related data
                var settingsToSave = new RuntimeSceneSettings();
                CopySceneSettings(runtimeSettings, settingsToSave);
                currentSceneProfilePath = path;
                json = JsonUtility.ToJson(settingsToSave, true);
            }
            else
            {
                // A PP profile is just the active target's look.
                json = JsonUtility.ToJson(ActivePP, true);
                ActivePPState.profilePath = path;

                // Update volume controller with post-processing settings
                if (Controller?.volumeController != null)
                {
                    Controller.volumeController.ApplyCurrentSettings(runtimeSettings);
                }
            }

            File.WriteAllText(path, json);

            // The saved profile is the new baseline for this tab.
            if (tabType == TabType.Scene)
                sceneBaselineJson = CanonicalSceneJson(runtimeSettings);
            else
                ActivePPState.baselineJson = ActivePP.ToCanonicalJson();
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
        UpdateConditionalRows();
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
            particlePP.ProfileName,
            feedPP.ProfileName
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

        // A working set from before the color settings has none: keep the inspector's.
        if (!file.settings.hasStyleColors)
            file.settings.CopyStyleColorsFrom(runtimeSettings);
        SetRuntimeSettings(file.settings.DeepCopy());

        currentSceneProfilePath = ResolveProfilePath(sceneProfilesDirectory, file.sceneProfileName);
        particlePP.profilePath = ResolveProfilePath(
            postProcessingProfilesDirectory,
            file.postProcessingProfileName
        );
        feedPP.profilePath = ResolveProfilePath(
            postProcessingProfilesDirectory,
            file.feedPostProcessingProfileName
        );
        sceneBaselineJson = ComputeProfileBaseline(currentSceneProfilePath, ProfileType.Scene);
        particlePP.baselineJson = ComputeProfileBaseline(
            particlePP.profilePath,
            ProfileType.PostProcessing
        );
        feedPP.baselineJson = ComputeProfileBaseline(
            feedPP.profilePath,
            ProfileType.PostProcessing
        );

        // Keep the last-used keys in step so a missing working set still falls back sensibly.
        if (!string.IsNullOrEmpty(currentSceneProfilePath))
            PlayerPrefs.SetString(lastUsedSceneProfileKey, file.sceneProfileName);
        if (!string.IsNullOrEmpty(particlePP.profilePath))
            PlayerPrefs.SetString(particlePP.lastUsedKey, file.postProcessingProfileName);
        if (!string.IsNullOrEmpty(feedPP.profilePath))
            PlayerPrefs.SetString(feedPP.lastUsedKey, file.feedPostProcessingProfileName);
        PlayerPrefs.Save();

        Debug.Log(
            $"[InGameSettingsMenu] Restored working set for '{ActiveSceneName}' "
                + $"(scene profile '{file.sceneProfileName}', particle PP profile '{file.postProcessingProfileName}', "
                + $"feed PP profile '{file.feedPostProcessingProfileName}', saved {file.savedAtUtc})."
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
            if (profileType == ProfileType.PostProcessing)
                return JsonUtility.FromJson<PostProcessSettings>(json).ToCanonicalJson();

            var loaded = JsonUtility.FromJson<RuntimeSceneSettings>(json);
            if (loaded.settingsVersion < RuntimeSceneSettings.CurrentSettingsVersion)
            {
                BodyScaling.ConvertLegacyProfileInPlace(loaded, json);
            }

            // Merge into a scratch copy so the merge code stays the single source of truth.
            var live = runtimeSettings;
            runtimeSettings = live.DeepCopy();
            MergeSceneSettings(loaded);
            string baseline = CanonicalSceneJson(runtimeSettings);
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

    private void UpdateDirtyState()
    {
        if (runtimeSettings == null)
            return;
        isSceneDirty =
            string.IsNullOrEmpty(currentSceneProfilePath)
            || CanonicalSceneJson(runtimeSettings) != sceneBaselineJson;
        foreach (var state in new[] { particlePP, feedPP })
        {
            state.isDirty =
                string.IsNullOrEmpty(state.profilePath)
                || state.get(runtimeSettings).ToCanonicalJson() != state.baselineJson;
        }
    }

    private void UpdateDirtyIndicators()
    {
        // Feed availability decides both the target switch and whether the feed's changes count.
        UpdatePostProcessingTargetAvailability();
        SetDirtyLabel(sceneDirtyLabel, isSceneDirty, currentSceneProfilePath);
        SetDirtyLabel(postProcessingDirtyLabel, ActivePPState.isDirty, ActivePPState.profilePath);
        SetTargetButtonText(postProcessingTargetParticlesButton, particlePP);
        SetTargetButtonText(postProcessingTargetFeedButton, feedPP);
        if (sceneTab != null)
            sceneTab.text = isSceneDirty ? "Scene *" : "Scene";
        if (postProcessingTab != null)
            postProcessingTab.text = IsPostProcessingDirty
                ? "Post Processing *"
                : "Post Processing";
    }

    /// <summary>
    /// Shows the Particles / Camera Feed switch only while <see cref="IsFeedTargetAvailable"/>
    /// (with one target there is nothing to switch), and falls back to the Particles target if
    /// the feed was selected when it became unavailable.
    /// </summary>
    private void UpdatePostProcessingTargetAvailability()
    {
        bool feedAvailable = IsFeedTargetAvailable;
        var targetRow = postProcessingTargetFeedButton?.parent;
        if (targetRow != null)
            targetRow.style.display = feedAvailable ? DisplayStyle.Flex : DisplayStyle.None;
        if (!feedAvailable && activePostProcessingTarget == PostProcessingTarget.Feed)
            SwitchPostProcessingTarget(PostProcessingTarget.Particles);
    }

    private static void SetTargetButtonText(Button button, PostProcessingTargetState state)
    {
        if (button != null)
            button.text = state.isDirty ? state.displayName + " *" : state.displayName;
    }

    private static void SetDirtyLabel(Label label, bool dirty, string profilePath)
    {
        if (label == null)
            return;
        label.style.display = dirty ? DisplayStyle.Flex : DisplayStyle.None;
        label.text = string.IsNullOrEmpty(profilePath) ? "Unsaved (no profile)" : "Unsaved changes";
    }

    public bool IsSceneDirty => isSceneDirty;
    public bool IsPostProcessingDirty =>
        particlePP.isDirty || (IsFeedTargetAvailable && feedPP.isDirty);

    /// <summary>
    /// Load the dropdown's profile, asking first when the tab has unsaved changes.
    /// <paramref name="previousDropdownValue"/> is restored on cancel (dropdown-driven loads).
    /// </summary>
    private void RequestLoadSelectedProfile(TabType tabType, string previousDropdownValue = null)
    {
        bool dirty = tabType == TabType.Scene ? isSceneDirty : ActivePPState.isDirty;
        string tabKey = tabType == TabType.Scene ? "scene" : "postprocessing";
        if (!dirty)
        {
            LoadSelectedProfile(tabKey);
            return;
        }

        string activeName =
            tabType == TabType.Scene ? CurrentSceneProfileName : ActivePPState.ProfileName;
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
        modal.AddToClassList("modal-overlay");

        var panel = new VisualElement();
        panel.AddToClassList("modal-panel");
        panel.style.width = 440;

        var title = new Label(titleText);
        title.AddToClassList("modal-title");
        panel.Add(title);

        var message = new Label(messageText);
        message.AddToClassList("modal-message");
        panel.Add(message);

        var buttons = new VisualElement();
        buttons.AddToClassList("modal-buttons");

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
        confirm.AddToClassList("modal-button");
        confirm.AddToClassList("primary");

        var cancel = new Button(() =>
        {
            Close();
            onCancel?.Invoke();
        });
        cancel.text = "Cancel";
        cancel.AddToClassList("modal-button");

        buttons.Add(cancel);
        buttons.Add(confirm);
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
