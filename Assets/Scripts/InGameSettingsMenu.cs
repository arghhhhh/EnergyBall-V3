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
        PostProcessing
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
    private DropdownField sceneProfileDropdown, postProcessingProfileDropdown;
    private Button sceneLoadButton, sceneSaveButton, sceneSaveAsButton;
    private Button postProcessingLoadButton, postProcessingSaveButton, postProcessingSaveAsButton;
    private Button closeButton;
    private Button sceneTab, postProcessingTab;

    private RuntimeSceneSettings runtimeSettings;
    private RuntimeSceneSettings originalSettings; // Backup for canceling changes
    private string currentSceneProfilePath = "";
    private string currentPostProcessingProfilePath = "";
    private string sceneProfilesDirectory;
    private string postProcessingProfilesDirectory;
    private string lastUsedSceneProfileKey = "LastUsedSceneProfile";
    private string lastUsedPostProcessingProfileKey = "LastUsedPostProcessingProfile";

    private readonly List<VisualElement> settingGroups = new();
    private readonly Dictionary<string, VisualElement> settingElements = new();
    private readonly List<Texture2D> curveTextures = new();
    private bool isModalOpen = false;

    public event Action<RuntimeSceneSettings> OnSettingsChanged;

    public bool IsMenuOpen => !settingsPanel.ClassListContains("hidden");

    public enum ProfileType
    {
        Scene,
        PostProcessing
    }

    private void Awake()
    {
        sceneProfilesDirectory = Path.Combine(Application.streamingAssetsPath, "SettingsProfiles", "Scene");
        postProcessingProfilesDirectory = Path.Combine(Application.streamingAssetsPath, "SettingsProfiles", "PostProcessing");

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

        RefreshSceneProfiles();
        RefreshPostProcessingProfiles();
        CreateSettingsUI();
    }

    /// <summary>
    /// Initialize scene-specific keys directly from controller if available
    /// </summary>
    private void InitializeSceneSpecificKeysFromController()
    {
        if (Controller != null)
        {
            lastUsedSceneProfileKey = Controller.GetSceneSpecificSceneProfileKey();
            lastUsedPostProcessingProfileKey = Controller.GetSceneSpecificPostProcessingProfileKey();
        }
    }

    /// <summary>
    /// Ensure scene-specific keys are updated before using them
    /// </summary>
    private void EnsureSceneSpecificKeys()
    {
        if (Controller != null && (lastUsedSceneProfileKey == "LastUsedSceneProfile" || lastUsedPostProcessingProfileKey == "LastUsedPostProcessingProfile"))
        {
            lastUsedSceneProfileKey = Controller.GetSceneSpecificSceneProfileKey();
            lastUsedPostProcessingProfileKey = Controller.GetSceneSpecificPostProcessingProfileKey();
        }
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M) && !isModalOpen && !RuntimeCurveEditorWindow.IsVisible)
        {
            ToggleMenu();
        }
    }

    private void InitializeRuntimeSettings()
    {
        if (Controller != null)
        {
            // Get settings from SceneController inspector values
            runtimeSettings = new RuntimeSceneSettings();
            Controller.CopyInspectorToRuntime(runtimeSettings);
            originalSettings = runtimeSettings.DeepCopy();

            // Subscribe to runtime settings changes
            runtimeSettings.OnAnyDebuggingSettingChanged += () => OnSettingsChanged?.Invoke(runtimeSettings);
        }
        else
        {
            // Create default runtime settings if controller is not available
            runtimeSettings = new RuntimeSceneSettings();
            originalSettings = runtimeSettings.DeepCopy();
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

        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        sceneSettingsPanel = root.Q<ScrollView>("SceneSettingsPanel");
        postProcessingPanel = root.Q<ScrollView>("PostProcessingPanel");

        // Scene tab controls
        var sceneTabContent = root.Q<VisualElement>("SceneTabContent");
        if (sceneTabContent != null)
        {
            sceneProfileDropdown = sceneTabContent.Q<DropdownField>("SceneProfileDropdown");
            sceneLoadButton = sceneTabContent.Q<Button>("SceneLoadButton");
            sceneSaveButton = sceneTabContent.Q<Button>("SceneSaveButton");
            sceneSaveAsButton = sceneTabContent.Q<Button>("SceneSaveAsButton");
        }

        // Post-processing tab controls
        var postProcessingTabContent = root.Q<VisualElement>("PostProcessingTabContent");
        if (postProcessingTabContent != null)
        {
            postProcessingProfileDropdown = postProcessingTabContent.Q<DropdownField>("PostProcessingProfileDropdown");
            postProcessingLoadButton = postProcessingTabContent.Q<Button>("PostProcessingLoadButton");
            postProcessingSaveButton = postProcessingTabContent.Q<Button>("PostProcessingSaveButton");
            postProcessingSaveAsButton = postProcessingTabContent.Q<Button>("PostProcessingSaveAsButton");
        }

        closeButton = root.Q<Button>("CloseButton");
        sceneTab = root.Q<Button>("SceneTab");
        postProcessingTab = root.Q<Button>("PostProcessingTab");

        // Setup button callbacks
        closeButton.clicked += CloseMenu;

        // Scene tab callbacks
        if (sceneLoadButton != null) sceneLoadButton.clicked += () => LoadSelectedProfile("scene");
        if (sceneSaveButton != null) sceneSaveButton.clicked += () => SaveCurrentProfile(TabType.Scene);
        if (sceneSaveAsButton != null) sceneSaveAsButton.clicked += () => ShowSaveAsDialog(TabType.Scene);

        // Post-processing tab callbacks
        if (postProcessingLoadButton != null) postProcessingLoadButton.clicked += () => LoadSelectedProfile("postprocessing");
        if (postProcessingSaveButton != null) postProcessingSaveButton.clicked += () => SaveCurrentProfile(TabType.PostProcessing);
        if (postProcessingSaveAsButton != null) postProcessingSaveAsButton.clicked += () => ShowSaveAsDialog(TabType.PostProcessing);

        sceneTab.clicked += () => SwitchTab("scene");
        postProcessingTab.clicked += () => SwitchTab("postprocessing");

        // Auto-load when dropdown selections change
        if (sceneProfileDropdown != null)
        {
            sceneProfileDropdown.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    LoadSelectedProfile("scene");
                }
            });
        }

        if (postProcessingProfileDropdown != null)
        {
            postProcessingProfileDropdown.RegisterValueChangedCallback(evt =>
            {
                if (!string.IsNullOrEmpty(evt.newValue))
                {
                    LoadSelectedProfile("postprocessing");
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
            if (tex != null) Destroy(tex);
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

        CreateFloatField(group, "G", () => runtimeSettings.g, v => runtimeSettings.g = v);
        CreateFloatField(group, "Max Towards Force", () => runtimeSettings.maxTowardsForce, v => runtimeSettings.maxTowardsForce = v);
        CreateFloatField(group, "Max Away Force", () => runtimeSettings.maxAwayFromForce, v => runtimeSettings.maxAwayFromForce = v);
        CreateFloatField(group, "Gravity Force Damper", () => runtimeSettings.gravityForceDamper, v => runtimeSettings.gravityForceDamper = v);
        CreateFloatField(group, "Stop Gravity Distance", () => runtimeSettings.stopGravityDistance, v => runtimeSettings.stopGravityDistance = v);
        CreateFloatField(group, "Stop Moving Distance", () => runtimeSettings.stopMovingDistance, v => runtimeSettings.stopMovingDistance = v);
        CreateFloatField(group, "Stop Velocity", () => runtimeSettings.stopVelocity, v => runtimeSettings.stopVelocity = v);
        CreateFloatField(group, "Attraction Radius Multiplier", () => runtimeSettings.attractionRadiusMultiplier, v => runtimeSettings.attractionRadiusMultiplier = v);
    }

    private void CreateHandsAttractionGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Hands Attraction", parentContainer);

        CreateCurveField(group, "Force To Middle", () => runtimeSettings.forceToMiddle, v => runtimeSettings.forceToMiddle = v);
        CreateFloatField(group, "Single Hand Open Force Damper", () => runtimeSettings.singleHandOpenForceDamper, v => runtimeSettings.singleHandOpenForceDamper = v);
        CreateFloatField(group, "Push Force", () => runtimeSettings.pushForce, v => runtimeSettings.pushForce = v);
        CreateFloatField(group, "Min Drag", () => runtimeSettings.minDrag, v => runtimeSettings.minDrag = v);
        CreateFloatField(group, "Max Drag", () => runtimeSettings.maxDrag, v => runtimeSettings.maxDrag = v);

        CreateCurveField(group, "Alignment Vector Strength", () => runtimeSettings.alignmentVectorStrength, v => runtimeSettings.alignmentVectorStrength = v);
        CreateFloatField(group, "Alignment Vector Strength Scaler", () => runtimeSettings.alignmentVectorStrengthScaler, v => runtimeSettings.alignmentVectorStrengthScaler = v);
        CreateFloatField(group, "Hand Push Scaler", () => runtimeSettings.handPushScaler, v => runtimeSettings.handPushScaler = v);
        CreateToggleField(group, "Pray To Activate", () => runtimeSettings.prayToActivate, v => runtimeSettings.prayToActivate = v);
        CreateFloatField(group, "Pray To Activate Distance", () => runtimeSettings.prayToActivateDistance, v => runtimeSettings.prayToActivateDistance = v);
    }

    private void CreateBoundaryDragGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Boundary Drag", parentContainer);

        CreateFloatField(group, "Boundary Distance Multiplier", () => runtimeSettings.addedBoundaryDistance, v => runtimeSettings.addedBoundaryDistance = v);
        CreateFloatField(group, "Boundary Outward Drag", () => runtimeSettings.boundaryOutwardDrag, v => runtimeSettings.boundaryOutwardDrag = v);
        CreateFloatField(group, "Out Of Bounds Reset Delay", () => runtimeSettings.outOfBoundsResetDelay, v => runtimeSettings.outOfBoundsResetDelay = v);
    }

    private void CreateIntrinsicPulsationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Intrinsic Pulsation", parentContainer);

        CreateSliderField(group, "Pulse Amount", () => runtimeSettings.pulseAmount, v => runtimeSettings.pulseAmount = v, 0f, 10f);
        CreateFloatField(group, "Pulse Speed", () => runtimeSettings.pulseSpeed, v => runtimeSettings.pulseSpeed = v);
        CreateFloatField(group, "Graph Limit", () => runtimeSettings.graphLimit, v => runtimeSettings.graphLimit = v);
        CreateFloatArrayField(group, "Pulse Frequencies", () => runtimeSettings.pulseFreqs, v => runtimeSettings.pulseFreqs = v);
    }

    private void CreateMovementPulsationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Movement-Based Pulsation", parentContainer);

        CreateToggleField(group, "Single Hand Scaling", () => runtimeSettings.singleHandScaling, v => runtimeSettings.singleHandScaling = v);
        CreateFloatField(group, "Minimum Unscaled Size", () => runtimeSettings.minimumUnscaledSize, v => runtimeSettings.minimumUnscaledSize = v);
        CreateSliderField(group, "Min Hand Displacement Per Frame", () => runtimeSettings.minHandDisplacementPerFrame, v => runtimeSettings.minHandDisplacementPerFrame = v, 0.0001f, 5f);
        CreateCurveField(group, "Distance Damper", () => runtimeSettings.distanceDamper, v => runtimeSettings.distanceDamper = v);
        CreateFloatField(group, "Pulse Scale Damper", () => runtimeSettings.pulseScaleDamper, v => runtimeSettings.pulseScaleDamper = v);
    }

    private void CreateMiscellaneousGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Miscellaneous", parentContainer);

        CreateFloatField(group, "Merge Size Scaler Damper", () => runtimeSettings.mergeSizeScalerDamper, v => runtimeSettings.mergeSizeScalerDamper = v);
        CreateFloatField(group, "Max Distance Between Hands", () => runtimeSettings.maxDistanceBetweenHands, v => runtimeSettings.maxDistanceBetweenHands = v);
        CreateFloatField(group, "Base Z Depth", () => runtimeSettings.baseZDepth, v => runtimeSettings.baseZDepth = v);
        CreateFloatField(group, "Default Unscaled Size", () => runtimeSettings.defaultUnscaledSize, v => runtimeSettings.defaultUnscaledSize = v);
        CreateFloatField(group, "Body Scale", () => runtimeSettings.bodyScale, v => runtimeSettings.bodyScale = v);
        CreateFloatField(group, "Max Distance From Camera", () => runtimeSettings.maxDistanceFromCamera, v => runtimeSettings.maxDistanceFromCamera = v);
    }

    private void CreateAnimationGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Animation", parentContainer);

        CreateFloatField(group, "Particle Initialization Delay", () => runtimeSettings.particleInitializationDelay, v => runtimeSettings.particleInitializationDelay = v);
        CreateFloatField(group, "Initialization Reset Delay", () => runtimeSettings.initializationResetDelay, v => runtimeSettings.initializationResetDelay = v);
        CreateSliderField(group, "Initialization Speed", () => runtimeSettings.initializationSpeed, v => runtimeSettings.initializationSpeed = v, 0f, 1f);
        CreateFloatField(group, "Metaball Radius Animation Duration", () => runtimeSettings.metaballRadiusAnimationDuration, v => runtimeSettings.metaballRadiusAnimationDuration = v);
        CreateFloatField(group, "Metaball Radius Animation Start Size", () => runtimeSettings.metaballRadiusAnimationStartSize, v => runtimeSettings.metaballRadiusAnimationStartSize = v);
    }

    private void CreatePostProcessingGroup(ScrollView parentContainer)
    {
        var bloomGroup = CreateGroup("Bloom", parentContainer);

        CreateFloatField(bloomGroup, "Bloom Threshold", () => runtimeSettings.bloomThreshold, v => runtimeSettings.bloomThreshold = v);
        CreateSliderField(bloomGroup, "Bloom Intensity", () => runtimeSettings.bloomIntensity, v => runtimeSettings.bloomIntensity = v, 0f, 3f);
        CreateSliderField(bloomGroup, "Bloom Scatter", () => runtimeSettings.bloomScatter, v => runtimeSettings.bloomScatter = v, 0f, 1f);

        var lensFlareGroup = CreateGroup("Screen Space Lens Flare", parentContainer);

        CreateSliderField(lensFlareGroup, "Intensity", () => runtimeSettings.lensFlareIntensity, v => runtimeSettings.lensFlareIntensity = v, 0f, 3f);
        CreateSliderField(lensFlareGroup, "Regular Multiplier (Flares)", () => runtimeSettings.lensFlareRegularMultiplier, v => runtimeSettings.lensFlareRegularMultiplier = v, 0f, 3f);
        CreateSliderField(lensFlareGroup, "Reversed Multiplier (Flares)", () => runtimeSettings.lensFlareReversedMultiplier, v => runtimeSettings.lensFlareReversedMultiplier = v, 0f, 3f);
        CreateSliderField(lensFlareGroup, "Multiplier (Streaks)", () => runtimeSettings.lensFlareStreaksMultiplier, v => runtimeSettings.lensFlareStreaksMultiplier = v, 0f, 3f);
        CreateSliderField(lensFlareGroup, "Length (Streaks)", () => runtimeSettings.lensFlareStreaksLength, v => runtimeSettings.lensFlareStreaksLength = v, 0f, 1f);
        CreateSliderField(lensFlareGroup, "Orientation (Streaks)", () => runtimeSettings.lensFlareStreaksOrientation, v => runtimeSettings.lensFlareStreaksOrientation = v, -180f, 180f);
        CreateSliderField(lensFlareGroup, "Threshold (Streaks)", () => runtimeSettings.lensFlareStreaksThreshold, v => runtimeSettings.lensFlareStreaksThreshold = v, 0f, 1f);
        CreateSliderField(lensFlareGroup, "Chromatic Aberration Intensity", () => runtimeSettings.lensFlareChromaticIntensity, v => runtimeSettings.lensFlareChromaticIntensity = v, 0f, 1f);

        var lensDistortionGroup = CreateGroup("Lens Distortion", parentContainer);

        CreateSliderField(lensDistortionGroup, "Intensity", () => runtimeSettings.lensDistortionIntensity, v => runtimeSettings.lensDistortionIntensity = v, -1f, 1f);
        CreateSliderField(lensDistortionGroup, "X Multiplier", () => runtimeSettings.lensDistortionXMultiplier, v => runtimeSettings.lensDistortionXMultiplier = v, 0f, 2f);
        CreateSliderField(lensDistortionGroup, "Y Multiplier", () => runtimeSettings.lensDistortionYMultiplier, v => runtimeSettings.lensDistortionYMultiplier = v, 0f, 2f);
        CreateSliderField(lensDistortionGroup, "Scale", () => runtimeSettings.lensDistortionScale, v => runtimeSettings.lensDistortionScale = v, 0.01f, 3f);
        CreateSliderField(lensDistortionGroup, "Center X", () => runtimeSettings.lensDistortionCenterX, v => runtimeSettings.lensDistortionCenterX = v, 0f, 1f);
        CreateSliderField(lensDistortionGroup, "Center Y", () => runtimeSettings.lensDistortionCenterY, v => runtimeSettings.lensDistortionCenterY = v, 0f, 1f);

        var colorAdjustmentsGroup = CreateGroup("Color Adjustments", parentContainer);

        CreateFloatField(colorAdjustmentsGroup, "Post Exposure", () => runtimeSettings.colorAdjustmentsPostExposure, v => runtimeSettings.colorAdjustmentsPostExposure = v);
        CreateSliderField(colorAdjustmentsGroup, "Contrast", () => runtimeSettings.colorAdjustmentsContrast, v => runtimeSettings.colorAdjustmentsContrast = v, -100f, 100f);
        CreateSliderField(colorAdjustmentsGroup, "Hue Shift", () => runtimeSettings.colorAdjustmentsHueShift, v => runtimeSettings.colorAdjustmentsHueShift = v, -180f, 180f);
        CreateSliderField(colorAdjustmentsGroup, "Saturation", () => runtimeSettings.colorAdjustmentsSaturation, v => runtimeSettings.colorAdjustmentsSaturation = v, -100f, 100f);

        var whiteBalanceGroup = CreateGroup("White Balance", parentContainer);

        CreateSliderField(whiteBalanceGroup, "Temperature", () => runtimeSettings.whiteBalanceTemperature, v => runtimeSettings.whiteBalanceTemperature = v, -100f, 100f);
        CreateSliderField(whiteBalanceGroup, "Tint", () => runtimeSettings.whiteBalanceTint, v => runtimeSettings.whiteBalanceTint = v, -100f, 100f);
    }

    private void CreateStyleGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Style", parentContainer);

        CreateToggleField(group, "Custom Colors", () => runtimeSettings.customColors, v => runtimeSettings.customColors = v);
        CreateToggleField(group, "Draw Skeleton", () => runtimeSettings.drawSkeleton, v => runtimeSettings.drawSkeleton = v);
        CreateToggleField(group, "Use Tracking State Colors", () => runtimeSettings.useTrackingStateColors, v => runtimeSettings.useTrackingStateColors = v);
    }

    private void CreateDebuggingGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Debugging", parentContainer);

        CreateToggleField(group, "Dummy Only Mode", () => runtimeSettings.dummyOnlyMode, v => runtimeSettings.dummyOnlyMode = v);
        CreateToggleField(group, "Show Sphere Mesh On Hand Collision", () => runtimeSettings.showSphereMeshOnHandCollision, v => runtimeSettings.showSphereMeshOnHandCollision = v);
        CreateToggleField(group, "Always Show Sphere Mesh", () => runtimeSettings.alwaysShowSphereMesh, v => runtimeSettings.alwaysShowSphereMesh = v);
        CreateToggleField(group, "Show Metaball Mesh", () => runtimeSettings.showMetaballMesh, v => runtimeSettings.showMetaballMesh = v);
        CreateToggleField(group, "Show Attraction Radius", () => runtimeSettings.showAttractionRadius, v => runtimeSettings.showAttractionRadius = v);
        CreateToggleField(group, "Show Hand Trail Distorters", () => runtimeSettings.showHandTrailDistorters, v => runtimeSettings.showHandTrailDistorters = v);
        CreateToggleField(group, "Show Secondary Attractor", () => runtimeSettings.showSecondaryAttractor, v => runtimeSettings.showSecondaryAttractor = v);
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

    private void CreateFloatField(VisualElement parent, string label, Func<float> getter, Action<float> setter)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");

        var field = new FloatField();
        field.AddToClassList("setting-input");
        field.value = getter();
        field.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            OnSettingsChanged?.Invoke(runtimeSettings);
        });

        row.Add(labelElement);
        row.Add(field);
        parent.Add(row);

        settingElements[label] = field;
    }

    private void CreateSliderField(VisualElement parent, string label, Func<float> getter, Action<float> setter, float min, float max)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");

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
            OnSettingsChanged?.Invoke(runtimeSettings);
        });

        inputContainer.Add(slider);
        inputContainer.Add(valueLabel);

        row.Add(labelElement);
        row.Add(inputContainer);
        parent.Add(row);

        settingElements[label] = slider;
        settingElements[label + "_ValueLabel"] = valueLabel;
    }

    private void CreateToggleField(VisualElement parent, string label, Func<bool> getter, Action<bool> setter)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");

        var toggle = new Toggle();
        toggle.AddToClassList("toggle");
        toggle.value = getter();
        toggle.RegisterValueChangedCallback(evt =>
        {
            setter(evt.newValue);
            OnSettingsChanged?.Invoke(runtimeSettings);
        });

        row.Add(labelElement);
        row.Add(toggle);
        parent.Add(row);

        settingElements[label] = toggle;
    }

    private void CreateCurveField(VisualElement parent, string label, Func<AnimationCurve> getter, Action<AnimationCurve> setter)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");

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
        thumbnail.schedule.Execute(() =>
        {
            if (hasRendered && !RuntimeCurveEditorWindow.IsVisible) return;

            var curve = getter();
            if (curve == null || curve.length == 0) return;

            int width = Mathf.Max(Mathf.RoundToInt(thumbnail.resolvedStyle.width), 4);
            int height = Mathf.Max(Mathf.RoundToInt(thumbnail.resolvedStyle.height), 4);
            if (width <= 4 || height <= 4) return;

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
        }).Every(200);

        // Click to open the curve editor popup
        thumbnail.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (RuntimeCurveEditorWindow.IsVisible) return;

            var curve = getter();
            if (curve == null) return;

            RuntimeCurveEditorWindow.Show(curve, changedCurve =>
            {
                setter(changedCurve);
                OnSettingsChanged?.Invoke(runtimeSettings);
            });
        });

        row.Add(labelElement);
        row.Add(thumbnail);
        parent.Add(row);

        settingElements[label] = thumbnail;
    }

    private static void RenderCurveToTexture(AnimationCurve curve, Texture2D tex, Color32 curveColor)
    {
        int width = tex.width;
        int height = tex.height;
        Color32 clear = new Color32(0, 0, 0, 0);

        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        // Compute curve value bounds by sampling
        float minTime = curve[0].time;
        float maxTime = curve[curve.length - 1].time;
        float timeRange = maxTime - minTime;
        if (timeRange < 0.001f) { timeRange = 1f; minTime -= 0.5f; maxTime += 0.5f; }

        float minVal = float.MaxValue, maxVal = float.MinValue;
        int sampleCount = width * 2;
        for (int i = 0; i <= sampleCount; i++)
        {
            float v = curve.Evaluate(minTime + timeRange * i / sampleCount);
            if (v < minVal) minVal = v;
            if (v > maxVal) maxVal = v;
        }
        float valRange = maxVal - minVal;
        if (valRange < 0.001f) { valRange = 1f; minVal -= 0.5f; }

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
            int y = Mathf.Clamp(Mathf.RoundToInt((v - minVal) / valRange * (height - 1)), 0, height - 1);

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

    private void CreateFloatArrayField(VisualElement parent, string label, Func<float[]> getter, Action<float[]> setter)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");

        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");

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

        var addButton = new Button(() => AddArrayElement(arrayContainer, getter, setter, collapseButton, countLabel));
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

    private void AddArrayElement(VisualElement container, Func<float[]> getter, Action<float[]> setter, Button collapseButton, Label countLabel)
    {
        var currentArray = getter();
        var newArray = new float[currentArray.Length + 1];
        Array.Copy(currentArray, newArray, currentArray.Length);
        newArray[newArray.Length - 1] = 1f;
        setter(newArray);
        RefreshFloatArray(container, newArray, setter, collapseButton, countLabel);
        OnSettingsChanged?.Invoke(runtimeSettings);
    }

    private void RefreshFloatArray(VisualElement container, float[] array, Action<float[]> setter, Button collapseButton, Label countLabel)
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
                OnSettingsChanged?.Invoke(runtimeSettings);
            });

            var removeButton = new Button(() =>
            {
                var newArray = new float[array.Length - 1];
                Array.Copy(array, 0, newArray, 0, index);
                Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
                setter(newArray);
                RefreshFloatArray(container, newArray, setter, collapseButton, countLabel);
                OnSettingsChanged?.Invoke(runtimeSettings);
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
        if (runtimeSettings == null) return;

        // Recreate the entire UI to ensure all values are current
        CreateSettingsUI();
    }

    private void RefreshSceneProfiles()
    {
        if (sceneProfileDropdown == null) return;

        // Ensure we're using scene-specific keys
        EnsureSceneSpecificKeys();

        var profileFiles = Directory.GetFiles(sceneProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        sceneProfileDropdown.choices = profileFiles;

        // Skip loading profile if refresh is suppressed (during save operations)
        if (isRefreshingSuppressed) return;

        // Try to restore last used scene profile for this specific scene
        string lastUsedProfile = PlayerPrefs.GetString(lastUsedSceneProfileKey, "");

        if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
        {
            sceneProfileDropdown.SetValueWithoutNotify(lastUsedProfile);
            LoadProfile(Path.Combine(sceneProfilesDirectory, lastUsedProfile + ".json"), ProfileType.Scene);
        }
        else if (profileFiles.Count > 0)
        {
            // If no scene-specific profile exists, use the first available but don't save it as preference yet
            sceneProfileDropdown.SetValueWithoutNotify(profileFiles[0]);
            LoadProfile(Path.Combine(sceneProfilesDirectory, profileFiles[0] + ".json"), ProfileType.Scene);
        }
    }

    private void RefreshPostProcessingProfiles()
    {
        if (postProcessingProfileDropdown == null) return;

        // Ensure we're using scene-specific keys
        EnsureSceneSpecificKeys();

        var profileFiles = Directory.GetFiles(postProcessingProfilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();

        postProcessingProfileDropdown.choices = profileFiles;

        // Try to restore last used post-processing profile for this specific scene
        string lastUsedProfile = PlayerPrefs.GetString(lastUsedPostProcessingProfileKey, "");
        if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
        {
            postProcessingProfileDropdown.SetValueWithoutNotify(lastUsedProfile);
            LoadProfile(Path.Combine(postProcessingProfilesDirectory, lastUsedProfile + ".json"), ProfileType.PostProcessing);
        }
        else if (profileFiles.Count > 0)
        {
            // If no scene-specific profile exists, use the first available but don't save it as preference yet
            postProcessingProfileDropdown.SetValueWithoutNotify(profileFiles[0]);
            LoadProfile(Path.Combine(postProcessingProfilesDirectory, profileFiles[0] + ".json"), ProfileType.PostProcessing);
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
            if (sceneProfileDropdown == null || string.IsNullOrEmpty(sceneProfileDropdown.value)) return;
            var profilePath = Path.Combine(sceneProfilesDirectory, sceneProfileDropdown.value + ".json");
            LoadProfile(profilePath, ProfileType.Scene);
        }
        else if (tabType == "postprocessing")
        {
            if (postProcessingProfileDropdown == null || string.IsNullOrEmpty(postProcessingProfileDropdown.value)) return;
            var profilePath = Path.Combine(postProcessingProfilesDirectory, postProcessingProfileDropdown.value + ".json");
            LoadProfile(profilePath, ProfileType.PostProcessing);
        }
    }

    private void LoadProfile(string path, ProfileType profileType)
    {
        if (!File.Exists(path)) return;

        try
        {
            var json = File.ReadAllText(path);
            var loadedSettings = JsonUtility.FromJson<RuntimeSceneSettings>(json);

            // Merge loaded settings based on profile type
            if (profileType == ProfileType.Scene)
            {
                // Load only scene settings, keep current post-processing settings
                MergeSceneSettings(loadedSettings);
                currentSceneProfilePath = path;

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

                // Save as last used post-processing profile
                string profileName = Path.GetFileNameWithoutExtension(path);
                PlayerPrefs.SetString(lastUsedPostProcessingProfileKey, profileName);
                PlayerPrefs.Save();

                // Update Volume Profile with post-processing settings (during play mode)
                if (Application.isPlaying && Controller?.volumeController != null)
                {
                    Controller.volumeController.ApplyCurrentSettings(runtimeSettings);
                }

#if UNITY_EDITOR
                // Save post-processing settings to persist to edit mode after play mode stops
                VolumeController.OnProfileSaved(runtimeSettings);
#endif
            }

            RefreshUI();
            OnSettingsChanged?.Invoke(runtimeSettings);
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
        runtimeSettings.minDrag = loadedSettings.minDrag;
        runtimeSettings.maxDrag = loadedSettings.maxDrag;
        runtimeSettings.addedBoundaryDistance = loadedSettings.addedBoundaryDistance;
        runtimeSettings.boundaryOutwardDrag = loadedSettings.boundaryOutwardDrag;
        runtimeSettings.outOfBoundsResetDelay = loadedSettings.outOfBoundsResetDelay;
        if (loadedSettings.alignmentVectorStrength != null && loadedSettings.alignmentVectorStrength.length > 0)
            runtimeSettings.alignmentVectorStrength = new AnimationCurve(loadedSettings.alignmentVectorStrength.keys);
        runtimeSettings.alignmentVectorStrengthScaler = loadedSettings.alignmentVectorStrengthScaler;
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
        runtimeSettings.minHandDisplacementPerFrame = loadedSettings.minHandDisplacementPerFrame;
        if (loadedSettings.distanceDamper != null && loadedSettings.distanceDamper.length > 0)
            runtimeSettings.distanceDamper = new AnimationCurve(loadedSettings.distanceDamper.keys);
        runtimeSettings.pulseScaleDamper = loadedSettings.pulseScaleDamper;
        runtimeSettings.mergeSizeScalerDamper = loadedSettings.mergeSizeScalerDamper;
        runtimeSettings.maxDistanceBetweenHands = loadedSettings.maxDistanceBetweenHands;
        runtimeSettings.baseZDepth = loadedSettings.baseZDepth;
        runtimeSettings.defaultUnscaledSize = loadedSettings.defaultUnscaledSize;
        runtimeSettings.bodyScale = loadedSettings.bodyScale;
        runtimeSettings.maxDistanceFromCamera = loadedSettings.maxDistanceFromCamera;

        // Animation
        runtimeSettings.particleInitializationDelay = loadedSettings.particleInitializationDelay;
        runtimeSettings.initializationResetDelay = loadedSettings.initializationResetDelay;
        runtimeSettings.initializationSpeed = loadedSettings.initializationSpeed;
        runtimeSettings.metaballRadiusAnimationDuration = loadedSettings.metaballRadiusAnimationDuration;
        runtimeSettings.metaballRadiusAnimationStartSize = loadedSettings.metaballRadiusAnimationStartSize;

        // Style settings
        runtimeSettings.customColors = loadedSettings.customColors;
        runtimeSettings.drawSkeleton = loadedSettings.drawSkeleton;
        runtimeSettings.useTrackingStateColors = loadedSettings.useTrackingStateColors;

        // Debug settings
        runtimeSettings.dummyOnlyMode = loadedSettings.dummyOnlyMode;
        runtimeSettings.showSphereMeshOnHandCollision = loadedSettings.showSphereMeshOnHandCollision;
        runtimeSettings.alwaysShowSphereMesh = loadedSettings.alwaysShowSphereMesh;
        runtimeSettings.showMetaballMesh = loadedSettings.showMetaballMesh;
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
        // Copy only non-post-processing settings to destination

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
        destination.minDrag = source.minDrag;
        destination.maxDrag = source.maxDrag;
        destination.addedBoundaryDistance = source.addedBoundaryDistance;
        destination.boundaryOutwardDrag = source.boundaryOutwardDrag;
        destination.outOfBoundsResetDelay = source.outOfBoundsResetDelay;
        destination.alignmentVectorStrength = new AnimationCurve(source.alignmentVectorStrength.keys);
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
        destination.minHandDisplacementPerFrame = source.minHandDisplacementPerFrame;
        destination.distanceDamper = new AnimationCurve(source.distanceDamper.keys);
        destination.pulseScaleDamper = source.pulseScaleDamper;
        destination.mergeSizeScalerDamper = source.mergeSizeScalerDamper;
        destination.maxDistanceBetweenHands = source.maxDistanceBetweenHands;
        destination.baseZDepth = source.baseZDepth;
        destination.defaultUnscaledSize = source.defaultUnscaledSize;
        destination.bodyScale = source.bodyScale;
        destination.maxDistanceFromCamera = source.maxDistanceFromCamera;

        // Animation
        destination.particleInitializationDelay = source.particleInitializationDelay;
        destination.initializationResetDelay = source.initializationResetDelay;
        destination.initializationSpeed = source.initializationSpeed;
        destination.metaballRadiusAnimationDuration = source.metaballRadiusAnimationDuration;
        destination.metaballRadiusAnimationStartSize = source.metaballRadiusAnimationStartSize;

        // Style settings
        destination.customColors = source.customColors;
        destination.drawSkeleton = source.drawSkeleton;
        destination.useTrackingStateColors = source.useTrackingStateColors;

        // Debug settings
        destination.dummyOnlyMode = source.dummyOnlyMode;
        destination.showSphereMeshOnHandCollision = source.showSphereMeshOnHandCollision;
        destination.alwaysShowSphereMesh = source.alwaysShowSphereMesh;
        destination.showMetaballMesh = source.showMetaballMesh;
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

    private void CopyPostProcessingSettings(RuntimeSceneSettings source, RuntimeSceneSettings destination)
    {
        // Copy only post-processing settings to destination

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
        destination.minHandDisplacementPerFrame = 0.0f;
        destination.distanceDamper = new AnimationCurve();
        destination.pulseScaleDamper = 0.0f;
        destination.mergeSizeScalerDamper = 0.0f;
        destination.maxDistanceBetweenHands = 0.0f;
        destination.baseZDepth = 0.0f;
        destination.defaultUnscaledSize = 0.0f;
        destination.bodyScale = 0.0f;
        destination.maxDistanceFromCamera = 0.0f;
        destination.particleInitializationDelay = 0.0f;
        destination.initializationResetDelay = 0.0f;
        destination.initializationSpeed = 0.0f;
        destination.metaballRadiusAnimationDuration = 0.0f;
        destination.metaballRadiusAnimationStartSize = 0.0f;
        // Note: metaballRadiusAnimationCurve is managed by SceneController inspector (set to default curve)
        destination.metaballRadiusAnimationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        destination.dummyOnlyMode = false;
        destination.drawSkeleton = false;
        destination.customColors = false;
        destination.showSphereMeshOnHandCollision = false;
        destination.alwaysShowSphereMesh = false;
        destination.showMetaballMesh = false;
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

#if UNITY_EDITOR
                // Save post-processing settings to persist to edit mode after play mode stops
                VolumeController.OnProfileSaved(runtimeSettings);
#endif
            }

            var json = JsonUtility.ToJson(settingsToSave, true);
            File.WriteAllText(path, json);
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
            runtimeSettings = newSettings.DeepCopy();

            // Only refresh UI if the panels are initialized (Start() has been called)
            if (sceneSettingsPanel != null && postProcessingPanel != null)
            {
                RefreshUI();
            }
        }
    }

}