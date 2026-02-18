using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RuntimeCurveEditor
{
    [System.Serializable]
    public class SerializableKeyframe
    {
        public float time,
            value,
            inTangent,
            outTangent;
        public float inWeight,
            outWeight;
        public int weightedMode;
        public int tangentMode;
    }

    [System.Serializable]
    public class SerializableCurvePreset
    {
        public string name;
        public SerializableKeyframe[] keys;
        public int preWrapMode;
        public int postWrapMode;
    }

    public static class RuntimeCurvePresetStorage
    {
        private static string PresetsDirectory =>
            Path.Combine(Application.streamingAssetsPath, "CurvePresets");

        public static List<RuntimeCurvePresets.CurvePreset> LoadAllPresets()
        {
            var result = new List<RuntimeCurvePresets.CurvePreset>();
            string dir = PresetsDirectory;

            if (!Directory.Exists(dir))
                return result;

            string[] files = Directory.GetFiles(dir, "*.json");
            foreach (string file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var data = JsonUtility.FromJson<SerializableCurvePreset>(json);
                    if (data == null || data.keys == null || data.keys.Length == 0)
                        continue;

                    AnimationCurve curve = DeserializeCurve(data);
                    result.Add(
                        new RuntimeCurvePresets.CurvePreset
                        {
                            name = data.name,
                            curve = curve,
                            isUserPreset = true,
                        }
                    );
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"Failed to load curve preset from {file}: {e.Message}");
                }
            }

            return result;
        }

        public static void SavePreset(string name, AnimationCurve curve)
        {
            try
            {
                string dir = PresetsDirectory;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = SerializeCurve(name, curve);
                string json = JsonUtility.ToJson(data, true);

                string sanitizedName = name;
                foreach (char c in Path.GetInvalidFileNameChars())
                    sanitizedName = sanitizedName.Replace(c, '_');

                string path = Path.Combine(dir, sanitizedName + ".json");
                File.WriteAllText(path, json);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save curve preset '{name}': {e.Message}");
            }
        }

        public static void DeletePreset(string name)
        {
            try
            {
                string dir = PresetsDirectory;
                if (!Directory.Exists(dir))
                    return;

                string sanitizedName = name;
                foreach (char c in Path.GetInvalidFileNameChars())
                    sanitizedName = sanitizedName.Replace(c, '_');

                string path = Path.Combine(dir, sanitizedName + ".json");
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to delete curve preset '{name}': {e.Message}");
            }
        }

        private static SerializableCurvePreset SerializeCurve(string name, AnimationCurve curve)
        {
            var data = new SerializableCurvePreset
            {
                name = name,
                keys = new SerializableKeyframe[curve.length],
                preWrapMode = (int)curve.preWrapMode,
                postWrapMode = (int)curve.postWrapMode,
            };

            for (int i = 0; i < curve.length; i++)
            {
                Keyframe kf = curve[i];
#pragma warning disable 0618
                data.keys[i] = new SerializableKeyframe
                {
                    time = kf.time,
                    value = kf.value,
                    inTangent = kf.inTangent,
                    outTangent = kf.outTangent,
                    inWeight = kf.inWeight,
                    outWeight = kf.outWeight,
                    weightedMode = (int)kf.weightedMode,
                    tangentMode = kf.tangentMode,
                };
#pragma warning restore 0618
            }

            return data;
        }

        private static AnimationCurve DeserializeCurve(SerializableCurvePreset data)
        {
            Keyframe[] keys = new Keyframe[data.keys.Length];
            for (int i = 0; i < data.keys.Length; i++)
            {
                var sk = data.keys[i];
#pragma warning disable 0618
                keys[i] = new Keyframe(sk.time, sk.value, sk.inTangent, sk.outTangent)
                {
                    inWeight = sk.inWeight,
                    outWeight = sk.outWeight,
                    weightedMode = (WeightedMode)sk.weightedMode,
                    tangentMode = sk.tangentMode,
                };
#pragma warning restore 0618
            }

            var curve = new AnimationCurve(keys);
            curve.preWrapMode = (WrapMode)data.preWrapMode;
            curve.postWrapMode = (WrapMode)data.postWrapMode;
            return curve;
        }
    }
}
