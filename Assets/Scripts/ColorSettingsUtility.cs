using System;
using UnityEngine;

/// <summary>
/// Copies and comparisons for the color settings. <see cref="Gradient"/> is a reference type
/// (like <see cref="AnimationCurve"/>), so every plumbing site must copy it - otherwise two
/// settings objects share one gradient and an edit in one silently changes the other.
/// </summary>
public static class ColorSettingsUtility
{
    public static Gradient Clone(Gradient source)
    {
        var copy = new Gradient();
        if (source == null)
            return copy;
        copy.SetKeys(source.colorKeys, source.alphaKeys);
        copy.mode = source.mode;
        copy.colorSpace = source.colorSpace;
        return copy;
    }

    public static Gradient[] Clone(Gradient[] source) =>
        source == null ? Array.Empty<Gradient>() : Array.ConvertAll(source, Clone);

    public static Color[] Clone(Color[] source) =>
        source == null ? Array.Empty<Color>() : (Color[])source.Clone();

    public static bool Same(Gradient a, Gradient b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.mode != b.mode)
            return false;

        var aColors = a.colorKeys;
        var bColors = b.colorKeys;
        if (aColors.Length != bColors.Length)
            return false;
        for (int i = 0; i < aColors.Length; i++)
        {
            if (aColors[i].color != bColors[i].color || aColors[i].time != bColors[i].time)
                return false;
        }

        var aAlphas = a.alphaKeys;
        var bAlphas = b.alphaKeys;
        if (aAlphas.Length != bAlphas.Length)
            return false;
        for (int i = 0; i < aAlphas.Length; i++)
        {
            if (aAlphas[i].alpha != bAlphas[i].alpha || aAlphas[i].time != bAlphas[i].time)
                return false;
        }
        return true;
    }

    public static bool Same(Gradient[] a, Gradient[] b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (!Same(a[i], b[i]))
                return false;
        }
        return true;
    }

    public static bool Same(Color[] a, Color[] b)
    {
        if (ReferenceEquals(a, b))
            return true;
        if (a == null || b == null || a.Length != b.Length)
            return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }
}
