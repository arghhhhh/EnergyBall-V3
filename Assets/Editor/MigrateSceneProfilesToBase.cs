using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-shot migration: turns legacy (settingsVersion 0) scene profiles - effective values
/// tuned at the file's own bodyScale - into base-at-1x profiles (settingsVersion 1), and
/// rewrites the SceneController inspector values of the two scenes from the migrated
/// Default / Default_Dummy profiles so a fresh machine (no last-used profile) starts at the
/// same look. Post-processing profiles are never touched.
/// </summary>
public static class MigrateSceneProfilesToBase
{
    private const string SceneProfilesDir = "Assets/StreamingAssets/SettingsProfiles/Scene";

    // scene path -> profile that seeds its SceneController inspector
    private static readonly (string scenePath, string profileName)[] SceneSeeds =
    {
        ("Assets/Energy Ball V3.unity", "Default"),
        ("Assets/Testing/Dummy Scene.unity", "Default_Dummy"),
    };

    [MenuItem("EnergyBall/Migrate Scene Profiles To Base")]
    public static void Migrate()
    {
        var log = new List<string>();
        int converted = 0,
            skipped = 0;

        foreach (var path in Directory.GetFiles(SceneProfilesDir, "*.json"))
        {
            string json = File.ReadAllText(path);
            var settings = JsonUtility.FromJson<RuntimeSceneSettings>(json);
            if (settings == null)
            {
                log.Add($"  ! {Path.GetFileName(path)}: could not parse - skipped");
                continue;
            }
            if (settings.settingsVersion >= RuntimeSceneSettings.CurrentSettingsVersion)
            {
                skipped++;
                continue;
            }

            float bodyScale = settings.bodyScale;
            BodyScaling.ConvertLegacyProfileInPlace(settings, json);
            File.WriteAllText(path, JsonUtility.ToJson(settings, true));
            converted++;
            log.Add($"  - {Path.GetFileName(path)}: v0 @ bodyScale {bodyScale} -> v1 base");
        }

        AssetDatabase.Refresh();

        foreach (var (scenePath, profileName) in SceneSeeds)
        {
            log.Add(RewriteSceneInspector(scenePath, profileName));
        }

        Debug.Log(
            $"[MigrateSceneProfilesToBase] converted {converted} profile(s), {skipped} already at v1.\n"
                + string.Join("\n", log)
        );
    }

    private static string RewriteSceneInspector(string scenePath, string profileName)
    {
        string profilePath = Path.Combine(SceneProfilesDir, profileName + ".json");
        if (!File.Exists(profilePath))
            return $"  ! {scenePath}: profile {profileName} not found - inspector not rewritten";
        if (!File.Exists(scenePath))
            return $"  ! {scenePath}: scene not found";

        var settings = JsonUtility.FromJson<RuntimeSceneSettings>(File.ReadAllText(profilePath));
        if (settings.settingsVersion < RuntimeSceneSettings.CurrentSettingsVersion)
            return $"  ! {profileName} is still v0 - inspector not rewritten";

        Scene scene = SceneManager.GetSceneByPath(scenePath);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        SceneController controller = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            controller = root.GetComponentInChildren<SceneController>(true);
            if (controller != null)
                break;
        }

        string result;
        if (controller == null)
        {
            result = $"  ! {scenePath}: no SceneController found";
        }
        else
        {
            Undo.RecordObject(controller, "Migrate inspector settings to base");
            controller.CopyRuntimeToInspector(settings);
            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            result =
                $"  - {scenePath}: SceneController inspector <- {profileName} (bodyScale {settings.bodyScale})";
        }

        if (openedHere)
            EditorSceneManager.CloseScene(scene, true);

        return result;
    }
}
