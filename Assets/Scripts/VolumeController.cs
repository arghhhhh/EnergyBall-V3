using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class VolumeController : MonoBehaviour
{
    private Volume volume;
    private Bloom bloom;
    private Vignette vignette;
    private ScreenSpaceLensFlare screenSpaceLensFlare;
    private ChromaticAberration chromaticAberration;
    private LensDistortion lensDistortion;
    private ColorAdjustments colorAdjustments;
    private WhiteBalance whiteBalance;

    // Keys for SessionState persistence
    private const string VOLUME_PATH_KEY = "VolumeController_ProfilePath";
    private const string ORIGINAL_PROFILE_JSON_KEY = "VolumeController_OriginalProfile";
    private const string CURRENT_PROFILE_JSON_KEY = "VolumeController_CurrentProfile";

    private void Start()
    {
        volume = GetComponent<Volume>();
        if (volume != null && volume.profile != null)
        {
#if UNITY_EDITOR
            // Store the asset path using SessionState for persistence
            string assetPath = AssetDatabase.GetAssetPath(volume.profile);
            if (!string.IsNullOrEmpty(assetPath))
            {
                SessionState.SetString(VOLUME_PATH_KEY, assetPath);
            }
            else
            {
                // Try to get the path another way
                string profileName = volume.profile.name;
                string[] guids = AssetDatabase.FindAssets($"{profileName} t:VolumeProfile");
                if (guids.Length > 0)
                {
                    assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    SessionState.SetString(VOLUME_PATH_KEY, assetPath);
                }
            }
#endif

            volume.profile.TryGet(out bloom);
            volume.profile.TryGet(out vignette);
            volume.profile.TryGet(out screenSpaceLensFlare);
            volume.profile.TryGet(out chromaticAberration);
            volume.profile.TryGet(out lensDistortion);
            volume.profile.TryGet(out colorAdjustments);
            volume.profile.TryGet(out whiteBalance);
        }

#if UNITY_EDITOR
        // Subscribe to play mode state changes to capture settings before exit
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

        // Delay the original profile capture to ensure InGameSettingsMenu is initialized
        EditorApplication.delayCall += () =>
        {
            // First, double-check the asset path
            if (string.IsNullOrEmpty(SessionState.GetString(VOLUME_PATH_KEY, "")))
            {
                if (volume != null && volume.profile != null)
                {
                    string retryPath = AssetDatabase.GetAssetPath(volume.profile);
                    if (!string.IsNullOrEmpty(retryPath))
                    {
                        SessionState.SetString(VOLUME_PATH_KEY, retryPath);
                    }
                }
            }

            // Then capture the original profile values after everything is initialized
            EditorApplication.delayCall += () =>
            {
                CaptureOriginalProfileValues();
            };
        };
#endif
    }

#if UNITY_EDITOR
    private void OnDestroy()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void CaptureOriginalProfileValues()
    {
        // Find the InGameSettingsMenu to get the current active profile settings at startup
        InGameSettingsMenu settingsMenu = FindFirstObjectByType<InGameSettingsMenu>();
        if (settingsMenu == null)
        {
            // Retry after another delay if the settings menu isn't ready yet
            EditorApplication.delayCall += () =>
            {
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        CaptureOriginalProfileValues();
                    };
                };
            };
            return;
        }

        var originalSettings = GetCurrentRuntimeSettings(settingsMenu);
        if (originalSettings != null)
        {
            string originalJson = JsonUtility.ToJson(originalSettings);
            SessionState.SetString(ORIGINAL_PROFILE_JSON_KEY, originalJson);
        }
    }

    private static RuntimeSceneSettings GetCurrentRuntimeSettings(InGameSettingsMenu settingsMenu)
    {
        // Use reflection to access the private runtimeSettings field
        var field = typeof(InGameSettingsMenu).GetField(
            "runtimeSettings",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );
        if (field != null)
        {
            return field.GetValue(settingsMenu) as RuntimeSceneSettings;
        }

        return null;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            // Check if the active profile has been modified and apply changes to Volume Profile
            string volumePath = SessionState.GetString(VOLUME_PATH_KEY, "");
            string originalJson = SessionState.GetString(ORIGINAL_PROFILE_JSON_KEY, "");

            if (!string.IsNullOrEmpty(volumePath) && !string.IsNullOrEmpty(originalJson))
            {
                // Use multiple delay calls to ensure we apply after Unity's restoration
                EditorApplication.delayCall += () =>
                {
                    EditorApplication.delayCall += () =>
                    {
                        EditorApplication.delayCall += () => CheckAndApplyProfileChanges();
                    };
                };
            }
            else
            {
                Debug.Log(
                    "VolumeController: Missing Volume Profile path or original settings, cannot check for changes"
                );
            }
        }
    }

    private static void CheckAndApplyProfileChanges()
    {
        string volumePath = SessionState.GetString(VOLUME_PATH_KEY, "");
        string originalJson = SessionState.GetString(ORIGINAL_PROFILE_JSON_KEY, "");
        string currentJson = SessionState.GetString(CURRENT_PROFILE_JSON_KEY, "");

        if (string.IsNullOrEmpty(volumePath) || string.IsNullOrEmpty(originalJson))
        {
            return;
        }

        if (string.IsNullOrEmpty(currentJson))
        {
            return;
        }

        var originalSettings = JsonUtility.FromJson<RuntimeSceneSettings>(originalJson);
        var currentSettings = JsonUtility.FromJson<RuntimeSceneSettings>(currentJson);

        if (originalSettings == null || currentSettings == null)
        {
            return;
        }

        // Check if the profile has been modified (focusing on post-processing values)
        bool hasProfileChanged = HasPostProcessingSettingsChanged(
            currentSettings,
            originalSettings
        );

        if (hasProfileChanged)
        {
            ApplySettingsToVolumeProfile(volumePath, currentSettings);
        }
    }

    private static bool HasPostProcessingSettingsChanged(
        RuntimeSceneSettings current,
        RuntimeSceneSettings original
    )
    {
        if (current == null || original == null)
            return false;

        // Check if any post-processing values have changed
        return current.bloomThreshold != original.bloomThreshold
            || current.bloomIntensity != original.bloomIntensity
            || current.bloomScatter != original.bloomScatter
            || current.lensFlareIntensity != original.lensFlareIntensity
            || current.lensFlareRegularMultiplier != original.lensFlareRegularMultiplier
            || current.lensFlareReversedMultiplier != original.lensFlareReversedMultiplier
            || current.lensFlareStreaksMultiplier != original.lensFlareStreaksMultiplier
            || current.lensFlareStreaksLength != original.lensFlareStreaksLength
            || current.lensFlareStreaksOrientation != original.lensFlareStreaksOrientation
            || current.lensFlareStreaksThreshold != original.lensFlareStreaksThreshold
            || current.lensFlareChromaticIntensity != original.lensFlareChromaticIntensity
            || current.lensDistortionIntensity != original.lensDistortionIntensity
            || current.lensDistortionXMultiplier != original.lensDistortionXMultiplier
            || current.lensDistortionYMultiplier != original.lensDistortionYMultiplier
            || current.lensDistortionScale != original.lensDistortionScale
            || current.lensDistortionCenterX != original.lensDistortionCenterX
            || current.lensDistortionCenterY != original.lensDistortionCenterY
            || current.colorAdjustmentsPostExposure != original.colorAdjustmentsPostExposure
            || current.colorAdjustmentsContrast != original.colorAdjustmentsContrast
            || current.colorAdjustmentsHueShift != original.colorAdjustmentsHueShift
            || current.colorAdjustmentsSaturation != original.colorAdjustmentsSaturation
            || current.whiteBalanceTemperature != original.whiteBalanceTemperature
            || current.whiteBalanceTint != original.whiteBalanceTint;
    }

    private static void ApplySettingsToVolumeProfile(
        string volumePath,
        RuntimeSceneSettings settings
    )
    {
        // Load the Volume Profile asset from the stored path
        VolumeProfile profileAsset = AssetDatabase.LoadAssetAtPath<VolumeProfile>(volumePath);
        if (profileAsset == null)
        {
            return;
        }

        // Apply all the settings to the Volume Profile asset
        if (profileAsset.TryGet(out Bloom profileBloom))
        {
            profileBloom.threshold.value = settings.bloomThreshold;
            profileBloom.intensity.value = settings.bloomIntensity;
            profileBloom.scatter.value = settings.bloomScatter;
            profileBloom.threshold.overrideState = true;
            profileBloom.intensity.overrideState = true;
            profileBloom.scatter.overrideState = true;
        }

        if (profileAsset.TryGet(out ScreenSpaceLensFlare profileLensFlare))
        {
            profileLensFlare.intensity.value = settings.lensFlareIntensity;
            profileLensFlare.firstFlareIntensity.value = settings.lensFlareRegularMultiplier;
            profileLensFlare.secondaryFlareIntensity.value = settings.lensFlareReversedMultiplier;
            profileLensFlare.streaksIntensity.value = settings.lensFlareStreaksMultiplier;
            profileLensFlare.streaksLength.value = settings.lensFlareStreaksLength;
            profileLensFlare.streaksOrientation.value = settings.lensFlareStreaksOrientation;
            profileLensFlare.streaksThreshold.value = settings.lensFlareStreaksThreshold;
            profileLensFlare.intensity.overrideState = true;
            profileLensFlare.firstFlareIntensity.overrideState = true;
            profileLensFlare.secondaryFlareIntensity.overrideState = true;
            profileLensFlare.streaksIntensity.overrideState = true;
            profileLensFlare.streaksLength.overrideState = true;
            profileLensFlare.streaksOrientation.overrideState = true;
            profileLensFlare.streaksThreshold.overrideState = true;
        }

        if (profileAsset.TryGet(out ChromaticAberration profileChromatic))
        {
            profileChromatic.intensity.value = settings.lensFlareChromaticIntensity;
            profileChromatic.intensity.overrideState = true;
        }

        if (profileAsset.TryGet(out LensDistortion profileLensDistortion))
        {
            profileLensDistortion.intensity.value = settings.lensDistortionIntensity;
            profileLensDistortion.xMultiplier.value = settings.lensDistortionXMultiplier;
            profileLensDistortion.yMultiplier.value = settings.lensDistortionYMultiplier;
            profileLensDistortion.scale.value = settings.lensDistortionScale;
            profileLensDistortion.center.value = new Vector2(
                settings.lensDistortionCenterX,
                settings.lensDistortionCenterY
            );
            profileLensDistortion.intensity.overrideState = true;
            profileLensDistortion.xMultiplier.overrideState = true;
            profileLensDistortion.yMultiplier.overrideState = true;
            profileLensDistortion.scale.overrideState = true;
            profileLensDistortion.center.overrideState = true;
        }

        if (profileAsset.TryGet(out ColorAdjustments profileColorAdjustments))
        {
            profileColorAdjustments.postExposure.value = settings.colorAdjustmentsPostExposure;
            profileColorAdjustments.contrast.value = settings.colorAdjustmentsContrast;
            profileColorAdjustments.hueShift.value = settings.colorAdjustmentsHueShift;
            profileColorAdjustments.saturation.value = settings.colorAdjustmentsSaturation;
            profileColorAdjustments.postExposure.overrideState = true;
            profileColorAdjustments.contrast.overrideState = true;
            profileColorAdjustments.hueShift.overrideState = true;
            profileColorAdjustments.saturation.overrideState = true;
        }

        if (profileAsset.TryGet(out WhiteBalance profileWhiteBalance))
        {
            profileWhiteBalance.temperature.value = settings.whiteBalanceTemperature;
            profileWhiteBalance.tint.value = settings.whiteBalanceTint;
            profileWhiteBalance.temperature.overrideState = true;
            profileWhiteBalance.tint.overrideState = true;
        }

        EditorUtility.SetDirty(profileAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
#endif

    public void UpdateBloomSettings(RuntimeSceneSettings settings)
    {
        if (bloom != null)
        {
            bloom.threshold.value = settings.bloomThreshold;
            bloom.intensity.value = settings.bloomIntensity;
            bloom.scatter.value = settings.bloomScatter;
        }
    }

    public void UpdateLensFlareSettings(RuntimeSceneSettings settings)
    {
        if (screenSpaceLensFlare != null)
        {
            screenSpaceLensFlare.intensity.value = settings.lensFlareIntensity;
            screenSpaceLensFlare.firstFlareIntensity.value = settings.lensFlareRegularMultiplier;
            screenSpaceLensFlare.secondaryFlareIntensity.value =
                settings.lensFlareReversedMultiplier;
            screenSpaceLensFlare.streaksIntensity.value = settings.lensFlareStreaksMultiplier;
            screenSpaceLensFlare.streaksLength.value = settings.lensFlareStreaksLength;
            screenSpaceLensFlare.streaksOrientation.value = settings.lensFlareStreaksOrientation;
            screenSpaceLensFlare.streaksThreshold.value = settings.lensFlareStreaksThreshold;
        }

        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = settings.lensFlareChromaticIntensity;
        }
    }

    public void UpdateLensDistortionSettings(RuntimeSceneSettings settings)
    {
        if (lensDistortion != null)
        {
            lensDistortion.intensity.value = settings.lensDistortionIntensity;
            lensDistortion.xMultiplier.value = settings.lensDistortionXMultiplier;
            lensDistortion.yMultiplier.value = settings.lensDistortionYMultiplier;
            lensDistortion.scale.value = settings.lensDistortionScale;
            lensDistortion.center.value = new Vector2(
                settings.lensDistortionCenterX,
                settings.lensDistortionCenterY
            );
        }
    }

    public void UpdateColorAdjustmentsSettings(RuntimeSceneSettings settings)
    {
        if (colorAdjustments != null)
        {
            colorAdjustments.postExposure.value = settings.colorAdjustmentsPostExposure;
            colorAdjustments.contrast.value = settings.colorAdjustmentsContrast;
            colorAdjustments.hueShift.value = settings.colorAdjustmentsHueShift;
            colorAdjustments.saturation.value = settings.colorAdjustmentsSaturation;
        }
    }

    public void UpdateWhiteBalanceSettings(RuntimeSceneSettings settings)
    {
        if (whiteBalance != null)
        {
            whiteBalance.temperature.value = settings.whiteBalanceTemperature;
            whiteBalance.tint.value = settings.whiteBalanceTint;
        }
    }

    public void ApplyCurrentSettings(RuntimeSceneSettings settings)
    {
        UpdateBloomSettings(settings);
        UpdateLensFlareSettings(settings);
        UpdateLensDistortionSettings(settings);
        UpdateColorAdjustmentsSettings(settings);
        UpdateWhiteBalanceSettings(settings);

        // Mark the volume profile as dirty so changes persist in the inspector
        MarkVolumeProfileDirty();
    }

#if UNITY_EDITOR
    public static void OnProfileSaved(RuntimeSceneSettings savedSettings)
    {
        // Only update stored settings when a profile is actually saved
        string currentJson = JsonUtility.ToJson(savedSettings);
        SessionState.SetString(CURRENT_PROFILE_JSON_KEY, currentJson);
    }
#endif

    private void MarkVolumeProfileDirty()
    {
#if UNITY_EDITOR
        if (volume != null && volume.profile != null)
        {
            EditorUtility.SetDirty(volume.profile);
        }
#endif
    }
}
