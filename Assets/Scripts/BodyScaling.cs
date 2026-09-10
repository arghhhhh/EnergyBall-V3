using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Derives the effective settings consumers read from the base settings the
/// menu / inspector / profiles hold: <c>effective = base × bodyScale^exp</c> for
/// every <see cref="BodyScaledAttribute"/> field of <see cref="RuntimeSceneSettings"/>
/// (recursing one level into [Serializable] member classes such as
/// <see cref="HandVfxSettings"/>). The reflection table is built once; the
/// per-change work is one <c>DeepCopy()</c> plus a handful of multiplies.
/// <c>bodyScale</c> itself is never scaled; a zero/negative bodyScale is treated as 1.
/// </summary>
public static class BodyScaling
{
    public readonly struct Entry
    {
        /// <summary>Field on RuntimeSceneSettings that holds the nested object, or null for a top-level field.</summary>
        public readonly FieldInfo Parent;
        public readonly FieldInfo Field;
        public readonly int Exponent;

        public Entry(FieldInfo parent, FieldInfo field, int exponent)
        {
            Parent = parent;
            Field = field;
            Exponent = exponent;
        }

        public string Path => Parent == null ? Field.Name : $"{Parent.Name}.{Field.Name}";
    }

    private static readonly List<Entry> entries = BuildTable();

    /// <summary>All scaled fields (top-level and one level nested), for tooling / migration.</summary>
    public static IReadOnlyList<Entry> Entries => entries;

    private static List<Entry> BuildTable()
    {
        var list = new List<Entry>();
        const BindingFlags flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var field in typeof(RuntimeSceneSettings).GetFields(flags))
        {
            var attr = field.GetCustomAttribute<BodyScaledAttribute>();
            if (attr != null)
            {
                if (!IsSupportedType(field.FieldType))
                {
                    Debug.LogError(
                        $"[BodyScaling] {field.Name} has [BodyScaled] but type {field.FieldType.Name} is not supported (float, Vector2, Vector3)."
                    );
                    continue;
                }
                list.Add(new Entry(null, field, attr.Exponent));
                continue;
            }

            // One level of recursion into [Serializable] member classes.
            var t = field.FieldType;
            if (
                t.IsClass
                && t != typeof(string)
                && !t.IsArray
                && t != typeof(AnimationCurve)
                && t.GetCustomAttribute<SerializableAttribute>() != null
            )
            {
                foreach (var nested in t.GetFields(flags))
                {
                    var nestedAttr = nested.GetCustomAttribute<BodyScaledAttribute>();
                    if (nestedAttr == null)
                        continue;
                    if (!IsSupportedType(nested.FieldType))
                    {
                        Debug.LogError(
                            $"[BodyScaling] {field.Name}.{nested.Name} has [BodyScaled] but type {nested.FieldType.Name} is not supported."
                        );
                        continue;
                    }
                    list.Add(new Entry(field, nested, nestedAttr.Exponent));
                }
            }
        }
        return list;
    }

    private static bool IsSupportedType(Type t) =>
        t == typeof(float) || t == typeof(Vector2) || t == typeof(Vector3);

    /// <summary>
    /// Returns a new settings object with every [BodyScaled] field multiplied by
    /// <c>bodyScale^exp</c>. Allocates (deep copy) — call on settings change, never per frame.
    /// </summary>
    public static RuntimeSceneSettings CreateEffective(RuntimeSceneSettings baseSettings)
    {
        var effective = baseSettings.DeepCopy();
        ScaleInPlace(effective, SafeBodyScale(baseSettings.bodyScale), +1);
        return effective;
    }

    /// <summary>Multiplies every [BodyScaled] field in place by <c>bodyScale^exp</c>.</summary>
    public static void ScaleInPlace(RuntimeSceneSettings settings, float bodyScale) =>
        ScaleInPlace(settings, SafeBodyScale(bodyScale), +1);

    /// <summary>
    /// Divides every [BodyScaled] field in place by <c>bodyScale^exp</c> — turns a
    /// legacy effective profile (tuned at its own bodyScale) into a base-at-1× profile.
    /// </summary>
    public static void UnscaleInPlace(RuntimeSceneSettings settings, float bodyScale) =>
        ScaleInPlace(settings, SafeBodyScale(bodyScale), -1);

    /// <summary>
    /// Converts a legacy (settingsVersion 0) scene profile - effective values tuned at the
    /// file's own bodyScale - into base-at-1x values, in place. Only fields actually present
    /// in <paramref name="json"/> are divided: a key the old file lacks already holds the C#
    /// default, which is a base value. Stamps <see cref="RuntimeSceneSettings.CurrentSettingsVersion"/>.
    /// Never call this on a post-processing profile.
    /// </summary>
    public static void ConvertLegacyProfileInPlace(RuntimeSceneSettings settings, string json)
    {
        if (settings == null)
            return;
        float bodyScale = SafeBodyScale(settings.bodyScale);
        if (!Mathf.Approximately(bodyScale, 1f))
        {
            foreach (var e in entries)
            {
                // Top-level key must exist; for nested entries the parent block must exist.
                string key = e.Parent != null ? e.Parent.Name : e.Field.Name;
                if (json != null && !json.Contains("\"" + key + "\""))
                    continue;
                ScaleEntry(settings, e, Mathf.Pow(bodyScale, -e.Exponent));
            }
        }
        settings.settingsVersion = RuntimeSceneSettings.CurrentSettingsVersion;
    }

    private static float SafeBodyScale(float bodyScale) => bodyScale > 0f ? bodyScale : 1f;

    private static void ScaleInPlace(RuntimeSceneSettings settings, float bodyScale, int sign)
    {
        if (settings == null || Mathf.Approximately(bodyScale, 1f))
            return;

        foreach (var e in entries)
            ScaleEntry(settings, e, Mathf.Pow(bodyScale, sign * e.Exponent));
    }

    private static void ScaleEntry(RuntimeSceneSettings settings, Entry e, float factor)
    {
        object target = settings;
        if (e.Parent != null)
        {
            target = e.Parent.GetValue(settings);
            if (target == null)
                return;
        }

        var type = e.Field.FieldType;
        if (type == typeof(float))
            e.Field.SetValue(target, (float)e.Field.GetValue(target) * factor);
        else if (type == typeof(Vector2))
            e.Field.SetValue(target, (Vector2)e.Field.GetValue(target) * factor);
        else if (type == typeof(Vector3))
            e.Field.SetValue(target, (Vector3)e.Field.GetValue(target) * factor);
    }
}
