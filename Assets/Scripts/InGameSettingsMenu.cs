using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class InGameSettingsMenu : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;
    [SerializeField] private SceneController sceneController;
    
    private VisualElement settingsPanel;
    private ScrollView sceneSettingsPanel;
    private ScrollView postProcessingPanel;
    private DropdownField profileDropdown;
    private Button loadButton, saveButton, saveAsButton, closeButton;
    private Button sceneTab, postProcessingTab;
    
    private RuntimeSceneSettings runtimeSettings;
    private RuntimeSceneSettings originalSettings; // Backup for canceling changes
    private string currentProfilePath = "";
    private string profilesDirectory;
    private string lastUsedProfileKey = "LastUsedProfile";
    
    private readonly List<VisualElement> settingGroups = new();
    private readonly Dictionary<string, VisualElement> settingElements = new();
    private bool isModalOpen = false;

    public event Action<RuntimeSceneSettings> OnSettingsChanged;

    private void Awake()
    {
        profilesDirectory = Path.Combine(Application.streamingAssetsPath, "SettingsProfiles");
        if (!Directory.Exists(profilesDirectory))
        {
            Directory.CreateDirectory(profilesDirectory);
        }

        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();
    }

    private void Start()
    {
        InitializeRuntimeSettings();
        SetupUI();
        RefreshProfileDropdown();
        CreateSettingsUI();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.M) && !isModalOpen)
        {
            ToggleMenu();
        }
    }

    private void InitializeRuntimeSettings()
    {
        if (sceneController?.so != null)
        {
            runtimeSettings = new RuntimeSceneSettings();
            runtimeSettings.CopyFromScriptableObject(sceneController.so);
            originalSettings = runtimeSettings.DeepCopy();
            
            // Subscribe to runtime settings changes
            runtimeSettings.OnAnyDebuggingSettingChanged += () => OnSettingsChanged?.Invoke(runtimeSettings);
        }
    }

    private void SetupUI()
    {
        var root = uiDocument.rootVisualElement;
        
        settingsPanel = root.Q<VisualElement>("SettingsPanel");
        sceneSettingsPanel = root.Q<ScrollView>("SceneSettingsPanel");
        postProcessingPanel = root.Q<ScrollView>("PostProcessingPanel");
        profileDropdown = root.Q<DropdownField>("ProfileDropdown");
        loadButton = root.Q<Button>("LoadButton");
        saveButton = root.Q<Button>("SaveButton");
        saveAsButton = root.Q<Button>("SaveAsButton");
        closeButton = root.Q<Button>("CloseButton");
        sceneTab = root.Q<Button>("SceneTab");
        postProcessingTab = root.Q<Button>("PostProcessingTab");
        
        // Setup button callbacks
        closeButton.clicked += CloseMenu;
        loadButton.clicked += LoadSelectedProfile;
        saveButton.clicked += SaveCurrentProfile;
        saveAsButton.clicked += ShowSaveAsDialog;
        sceneTab.clicked += () => SwitchTab("scene");
        postProcessingTab.clicked += () => SwitchTab("postprocessing");
        
        // Auto-load when dropdown selection changes
        profileDropdown.RegisterValueChangedCallback(evt => {
            if (!string.IsNullOrEmpty(evt.newValue))
            {
                LoadSelectedProfile();
            }
        });
    }

    private void CreateSettingsUI()
    {
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
        CreateIntrinsicPulsationGroup(sceneSettingsPanel);
        CreateMovementPulsationGroup(sceneSettingsPanel);
        CreateMiscellaneousGroup(sceneSettingsPanel);
        CreateAnimationGroup(sceneSettingsPanel);
        CreateDebuggingGroup(sceneSettingsPanel);
    }

    private void CreatePostProcessingContent()
    {
        CreatePostProcessingGroup(postProcessingPanel);
    }

    private void SwitchTab(string tabName)
    {
        if (tabName == "scene")
        {
            sceneTab.AddToClassList("active");
            postProcessingTab.RemoveFromClassList("active");
            sceneSettingsPanel.AddToClassList("active");
            postProcessingPanel.RemoveFromClassList("active");
        }
        else if (tabName == "postprocessing")
        {
            sceneTab.RemoveFromClassList("active");
            postProcessingTab.AddToClassList("active");
            sceneSettingsPanel.RemoveFromClassList("active");
            postProcessingPanel.AddToClassList("active");
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

    private void CreateDebuggingGroup(ScrollView parentContainer)
    {
        var group = CreateGroup("Debugging", parentContainer);
        
        CreateToggleField(group, "Dummy Only Mode", () => runtimeSettings.dummyOnlyMode, v => runtimeSettings.dummyOnlyMode = v);
        CreateToggleField(group, "Show Sphere Mesh On Hand Collision", () => runtimeSettings.showSphereMeshOnHandCollision, v => runtimeSettings.showSphereMeshOnHandCollision = v);
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
        field.RegisterValueChangedCallback(evt => {
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
        
        slider.RegisterValueChangedCallback(evt => {
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
        toggle.AddToClassList("setting-input");
        toggle.value = getter();
        toggle.RegisterValueChangedCallback(evt => {
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
        
        // Create a button that will show curve info and allow basic editing
        var curveButton = new Button();
        curveButton.AddToClassList("setting-input");
        curveButton.text = $"Curve ({getter().keys.Length} keys)";
        
        // For now, create a simple popup with curve presets
        curveButton.clicked += () => ShowCurveEditor(label, getter(), setter);
        
        var container = new VisualElement();
        container.Add(labelElement);
        container.Add(curveButton);
        row.Add(container);
        parent.Add(row);
        
        settingElements[label] = curveButton;
    }
    
    private void ShowCurveEditor(string curveName, AnimationCurve currentCurve, Action<AnimationCurve> setter)
    {
        isModalOpen = true;
        
        // Create a simple modal with curve presets and basic editing
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
        
        var title = new Label($"Edit {curveName}");
        title.style.fontSize = 18;
        title.style.color = Color.white;
        title.style.marginBottom = 15;
        panel.Add(title);
        
        // Add preset buttons
        var presetsLabel = new Label("Presets:");
        presetsLabel.style.color = Color.white;
        presetsLabel.style.marginBottom = 10;
        panel.Add(presetsLabel);
        
        var presetContainer = new VisualElement();
        presetContainer.style.flexDirection = FlexDirection.Row;
        presetContainer.style.marginBottom = 15;
        
        var linearButton = new Button(() => {
            setter(AnimationCurve.Linear(0, 0, 1, 1));
            OnSettingsChanged?.Invoke(runtimeSettings);
            UpdateCurveButton(curveName);
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        linearButton.text = "Linear";
        presetContainer.Add(linearButton);
        
        var easeInButton = new Button(() => {
            setter(AnimationCurve.EaseInOut(0, 0, 1, 1));
            OnSettingsChanged?.Invoke(runtimeSettings);
            UpdateCurveButton(curveName);
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        easeInButton.text = "Ease In/Out";
        presetContainer.Add(easeInButton);
        
        var constantButton = new Button(() => {
            setter(new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1)));
            OnSettingsChanged?.Invoke(runtimeSettings);
            UpdateCurveButton(curveName);
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        constantButton.text = "Constant";
        presetContainer.Add(constantButton);
        
        panel.Add(presetContainer);
        
        // Add current curve info
        var infoLabel = new Label($"Current: {currentCurve.keys.Length} keyframes");
        infoLabel.style.color = Color.gray;
        infoLabel.style.marginBottom = 15;
        panel.Add(infoLabel);
        
        // Close button
        var closeButton = new Button(() => {
            settingsPanel.Remove(modal);
            isModalOpen = false;
        });
        closeButton.text = "Close";
        closeButton.style.alignSelf = Align.Center;
        panel.Add(closeButton);
        
        modal.Add(panel);
        settingsPanel.Add(modal);
    }
    
    private void UpdateCurveButton(string curveName)
    {
        if (settingElements.TryGetValue(curveName, out var element) && element is Button button)
        {
            var curve = GetCurveByName(curveName);
            if (curve != null)
            {
                button.text = $"Curve ({curve.keys.Length} keys)";
            }
        }
    }
    
    private AnimationCurve GetCurveByName(string name)
    {
        return name switch
        {
            "Force To Middle" => runtimeSettings.forceToMiddle,
            "Alignment Vector Strength" => runtimeSettings.alignmentVectorStrength,
            "Distance Damper" => runtimeSettings.distanceDamper,
            _ => null
        };
    }

    private void CreateFloatArrayField(VisualElement parent, string label, Func<float[]> getter, Action<float[]> setter)
    {
        var row = new VisualElement();
        row.AddToClassList("setting-row");
        
        var labelElement = new Label(label);
        labelElement.AddToClassList("setting-label");
        
        var arrayContainer = new VisualElement();
        arrayContainer.AddToClassList("array-container");
        
        var addButton = new Button(() => AddArrayElement(arrayContainer, getter, setter));
        addButton.text = "Add Element";
        addButton.AddToClassList("array-button");
        
        arrayContainer.Add(addButton);
        RefreshFloatArray(arrayContainer, getter(), setter);
        
        var container = new VisualElement();
        container.Add(labelElement);
        container.Add(arrayContainer);
        row.Add(container);
        parent.Add(row);
        
        settingElements[label] = arrayContainer;
    }

    private void AddArrayElement(VisualElement container, Func<float[]> getter, Action<float[]> setter)
    {
        var currentArray = getter();
        var newArray = new float[currentArray.Length + 1];
        Array.Copy(currentArray, newArray, currentArray.Length);
        newArray[newArray.Length - 1] = 1f;
        setter(newArray);
        RefreshFloatArray(container, newArray, setter);
        OnSettingsChanged?.Invoke(runtimeSettings);
    }

    private void RefreshFloatArray(VisualElement container, float[] array, Action<float[]> setter)
    {
        // Clear existing elements except the add button
        var addButton = container.Children().First();
        container.Clear();
        container.Add(addButton);
        
        for (int i = 0; i < array.Length; i++)
        {
            int index = i; // Capture for closure
            var elementRow = new VisualElement();
            elementRow.AddToClassList("array-element");
            
            var field = new FloatField();
            field.AddToClassList("array-element-input");
            field.value = array[index];
            field.RegisterValueChangedCallback(evt => {
                array[index] = evt.newValue;
                setter(array);
                OnSettingsChanged?.Invoke(runtimeSettings);
            });
            
            var removeButton = new Button(() => {
                var newArray = new float[array.Length - 1];
                Array.Copy(array, 0, newArray, 0, index);
                Array.Copy(array, index + 1, newArray, index, array.Length - index - 1);
                setter(newArray);
                RefreshFloatArray(container, newArray, setter);
                OnSettingsChanged?.Invoke(runtimeSettings);
            });
            removeButton.text = "-";
            removeButton.AddToClassList("array-button");
            
            elementRow.Add(field);
            elementRow.Add(removeButton);
            container.Add(elementRow);
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
    }

    private void CloseMenu()
    {
        settingsPanel.AddToClassList("hidden");
    }

    private void RefreshUI()
    {
        if (runtimeSettings == null) return;
        
        // Recreate the entire UI to ensure all values are current
        CreateSettingsUI();
    }

    private void RefreshProfileDropdown()
    {
        var profileFiles = Directory.GetFiles(profilesDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .ToList();
        
        profileDropdown.choices = profileFiles;
        
        // Try to restore last used profile
        string lastUsedProfile = PlayerPrefs.GetString(lastUsedProfileKey, "");
        if (!string.IsNullOrEmpty(lastUsedProfile) && profileFiles.Contains(lastUsedProfile))
        {
            profileDropdown.value = lastUsedProfile;
            LoadProfile(Path.Combine(profilesDirectory, lastUsedProfile + ".json"));
        }
        else if (profileFiles.Count > 0)
        {
            profileDropdown.value = profileFiles[0];
            LoadProfile(Path.Combine(profilesDirectory, profileFiles[0] + ".json"));
        }
    }

    private void LoadSelectedProfile()
    {
        if (string.IsNullOrEmpty(profileDropdown.value)) return;
        
        var profilePath = Path.Combine(profilesDirectory, profileDropdown.value + ".json");
        LoadProfile(profilePath);
    }

    private void LoadProfile(string path)
    {
        if (!File.Exists(path)) return;
        
        try
        {
            var json = File.ReadAllText(path);
            var loadedSettings = JsonUtility.FromJson<RuntimeSceneSettings>(json);
            
            runtimeSettings = loadedSettings;
            currentProfilePath = path;
            
            // Save as last used profile
            string profileName = Path.GetFileNameWithoutExtension(path);
            PlayerPrefs.SetString(lastUsedProfileKey, profileName);
            PlayerPrefs.Save();
            
            RefreshUI();
            OnSettingsChanged?.Invoke(runtimeSettings);
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to load profile: {e.Message}");
        }
    }

    private void SaveCurrentProfile()
    {
        if (string.IsNullOrEmpty(currentProfilePath))
        {
            ShowSaveAsDialog();
            return;
        }
        
        SaveProfile(currentProfilePath);
    }

    private void ShowSaveAsDialog()
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
        
        var saveButton = new Button(() => {
            var profileName = nameField.value.Trim();
            if (!string.IsNullOrEmpty(profileName))
            {
                // Remove invalid filename characters
                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    profileName = profileName.Replace(c, '_');
                }
                
                SaveAsNewProfile(profileName);
                settingsPanel.Remove(modal);
                isModalOpen = false;
            }
        });
        saveButton.text = "Save";
        saveButton.style.marginRight = 10;
        saveButton.style.paddingLeft = 15;
        saveButton.style.paddingRight = 15;
        
        var cancelButton = new Button(() => {
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
        nameField.RegisterCallback<KeyDownEvent>(evt => {
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
                    
                    SaveAsNewProfile(profileName);
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

    private void SaveAsNewProfile(string profileName)
    {
        var profilePath = Path.Combine(profilesDirectory, profileName + ".json");
        
        SaveProfile(profilePath);
        RefreshProfileDropdown();
        profileDropdown.value = profileName;
        
        // Save as last used profile
        PlayerPrefs.SetString(lastUsedProfileKey, profileName);
        PlayerPrefs.Save();
    }

    private void SaveProfile(string path)
    {
        try
        {
            var json = JsonUtility.ToJson(runtimeSettings, true);
            File.WriteAllText(path, json);
            currentProfilePath = path;
            
            Debug.Log($"Profile saved: {Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to save profile: {e.Message}");
        }
    }

    public RuntimeSceneSettings GetCurrentSettings()
    {
        return runtimeSettings;
    }
}