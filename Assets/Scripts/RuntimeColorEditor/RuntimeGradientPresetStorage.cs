using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuntimeColorEditor
{
    [Serializable]
    public class GradientPreset
    {
        public string name = "";
        public Gradient gradient = new Gradient();
    }

    /// <summary>One library file: an ordered list of presets (names may be empty, as in the editor).</summary>
    [Serializable]
    public class GradientPresetLibrary
    {
        public List<GradientPreset> presets = new List<GradientPreset>();
    }

    /// <summary>
    /// Gradient preset libraries for the runtime gradient editor, like the editor's .gradients
    /// libraries: one JSON file per library in StreamingAssets/GradientPresets. The library
    /// and view mode in use are remembered in PlayerPrefs.
    /// </summary>
    public static class RuntimeGradientPresetStorage
    {
        public const string DefaultLibraryName = "Default";
        private const string LibraryPrefKey = "RuntimeGradientPresets.Library";
        private const string ListViewPrefKey = "RuntimeGradientPresets.ListView";

        public static string PresetsDirectory =>
            Path.Combine(Application.streamingAssetsPath, "GradientPresets");

        public static string CurrentLibraryName
        {
            get => PlayerPrefs.GetString(LibraryPrefKey, DefaultLibraryName);
            set
            {
                PlayerPrefs.SetString(LibraryPrefKey, value);
                PlayerPrefs.Save();
            }
        }

        public static bool ListView
        {
            get => PlayerPrefs.GetInt(ListViewPrefKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(ListViewPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Library names (file names), Default first.</summary>
        public static List<string> ListLibraries()
        {
            var names = new List<string>();
            if (Directory.Exists(PresetsDirectory))
            {
                foreach (string file in Directory.GetFiles(PresetsDirectory, "*.json"))
                    names.Add(Path.GetFileNameWithoutExtension(file));
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            names.Remove(DefaultLibraryName);
            names.Insert(0, DefaultLibraryName);
            return names;
        }

        /// <summary>Loads a library; a missing or unreadable file gives an empty one.</summary>
        public static GradientPresetLibrary Load(string libraryName)
        {
            string path = GetPath(libraryName);
            if (!File.Exists(path))
                return new GradientPresetLibrary();
            try
            {
                var library = JsonUtility.FromJson<GradientPresetLibrary>(File.ReadAllText(path));
                if (library?.presets == null)
                    return new GradientPresetLibrary();
                library.presets.RemoveAll(p => p == null || p.gradient == null);
                foreach (var preset in library.presets)
                    preset.name ??= "";
                return library;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load gradient presets from {path}: {e.Message}");
                return new GradientPresetLibrary();
            }
        }

        public static void Save(string libraryName, GradientPresetLibrary library)
        {
            try
            {
                Directory.CreateDirectory(PresetsDirectory);
                File.WriteAllText(GetPath(libraryName), JsonUtility.ToJson(library, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save gradient presets '{libraryName}': {e.Message}");
            }
        }

        private static string GetPath(string libraryName)
        {
            string safeName = libraryName;
            foreach (char c in Path.GetInvalidFileNameChars())
                safeName = safeName.Replace(c, '_');
            return Path.Combine(PresetsDirectory, safeName + ".json");
        }
    }
}
