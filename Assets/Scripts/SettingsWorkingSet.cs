using System;
using System.IO;
using UnityEngine;

/// <summary>
/// The per-scene "working set": the single latest copy of the settings, wherever they were last
/// changed (in-game menu, SceneController inspector in play mode OR edit mode, profile load).
///
/// Every change writes it immediately, so there is never a merge at a play-mode boundary - the
/// file is always the newest state. On play start the menu restores from it (instead of
/// auto-loading the last-used profile); on play exit the editor hook in SceneController copies
/// it back into the inspector twins. "Dirty" means the working set differs from the profile it
/// was derived from (<see cref="sceneProfileName"/> / <see cref="postProcessingProfileName"/>).
///
/// Stored under Application.persistentDataPath (StreamingAssets is read-only in builds), keyed
/// by scene name like the last-used-profile PlayerPrefs keys.
/// </summary>
public static class SettingsWorkingSet
{
    [Serializable]
    public class File
    {
        public int fileVersion = 1;
        public string sceneName = "";
        public string sceneProfileName = "";
        public string postProcessingProfileName = "";
        public string savedAtUtc = "";
        public RuntimeSceneSettings settings;
    }

    private const string DirectoryName = "SettingsWorkingSet";

    public static string GetPath(string sceneName)
    {
        string safeName = sceneName;
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');
        return Path.Combine(Application.persistentDataPath, DirectoryName, safeName + ".json");
    }

    public static bool Exists(string sceneName) => System.IO.File.Exists(GetPath(sceneName));

    /// <summary>Loads the working set for a scene. Returns null when none exists or it is unreadable.</summary>
    public static File Load(string sceneName)
    {
        string path = GetPath(sceneName);
        if (!System.IO.File.Exists(path))
            return null;
        try
        {
            var file = JsonUtility.FromJson<File>(System.IO.File.ReadAllText(path));
            if (file == null || file.settings == null)
                return null;
            // The working set is always written as base-at-1x; a v0 payload can only come from
            // a hand-edited file - refuse it rather than silently mis-scaling everything.
            if (file.settings.settingsVersion < RuntimeSceneSettings.CurrentSettingsVersion)
            {
                Debug.LogWarning(
                    $"[SettingsWorkingSet] '{path}' holds a legacy settings payload - ignoring it."
                );
                return null;
            }
            return file;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SettingsWorkingSet] Failed to read '{path}': {e.Message}");
            return null;
        }
    }

    public static void Save(
        string sceneName,
        RuntimeSceneSettings settings,
        string sceneProfileName,
        string postProcessingProfileName
    )
    {
        if (settings == null)
            return;
        try
        {
            var file = new File
            {
                sceneName = sceneName,
                sceneProfileName = sceneProfileName ?? "",
                postProcessingProfileName = postProcessingProfileName ?? "",
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                settings = settings.DeepCopy(),
            };
            file.settings.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;

            string path = GetPath(sceneName);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            System.IO.File.WriteAllText(path, JsonUtility.ToJson(file, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SettingsWorkingSet] Failed to write working set: {e.Message}");
        }
    }

    public static void Delete(string sceneName)
    {
        string path = GetPath(sceneName);
        if (System.IO.File.Exists(path))
            System.IO.File.Delete(path);
    }
}
