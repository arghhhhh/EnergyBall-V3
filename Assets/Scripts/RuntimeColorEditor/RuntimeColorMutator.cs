using UnityEngine;

namespace RuntimeColorEditor
{
    public enum RgbaChannel
    {
        R,
        G,
        B,
        A,
    }

    public enum HsvChannel
    {
        H,
        S,
        V,
    }

    /// <summary>
    /// The color model behind the runtime color picker, matching the Unity editor's picker:
    /// an HDR color is split into an 8-bit base color plus an exposure (intensity, in stops),
    /// so the RGB / HSV / hex controls edit the base and the Intensity control scales it.
    /// </summary>
    public class RuntimeColorMutator
    {
        // An over-exposed color is decomposed so its brightest channel lands on this byte,
        // leaving headroom for the HSV value slider (same constant as the editor).
        private const byte MaxByteForOverexposedColor = 191;

        private readonly Color originalColor;
        private Color hdrBaseColor;
        private readonly byte[] color = new byte[4];
        private readonly float[] colorHdr = new float[4];
        private readonly float[] hsv = new float[3];
        private float exposureValue;
        private float baseExposureValue;

        public RuntimeColorMutator(Color originalColor)
        {
            this.originalColor = originalColor;
            Reset();
        }

        public Color OriginalColor => originalColor;

        /// <summary>The 8-bit base color (exposure removed).</summary>
        public Color32 BaseColor => new Color32(color[0], color[1], color[2], color[3]);

        /// <summary>The full color, exposure applied. This is the value the picker outputs.</summary>
        public Color ExposureAdjustedColor =>
            new Color(colorHdr[0], colorHdr[1], colorHdr[2], colorHdr[3]);

        public float ExposureValue
        {
            get => exposureValue;
            set
            {
                if (Mathf.Approximately(exposureValue, value))
                    return;
                exposureValue = value;
                Color scaled = hdrBaseColor * Mathf.Pow(2f, exposureValue - baseExposureValue);
                colorHdr[0] = ClampFinite(scaled.r);
                colorHdr[1] = ClampFinite(scaled.g);
                colorHdr[2] = ClampFinite(scaled.b);
            }
        }

        public void Reset()
        {
            colorHdr[0] = originalColor.r;
            colorHdr[1] = originalColor.g;
            colorHdr[2] = originalColor.b;
            colorHdr[3] = originalColor.a;
            hdrBaseColor = ExposureAdjustedColor;
            OnHdrChannelChanged(-1);
            baseExposureValue = exposureValue;
        }

        public byte GetChannel(RgbaChannel channel) => color[(int)channel];

        public float GetChannelNormalized(RgbaChannel channel) => color[(int)channel] / 255f;

        public void SetChannel(RgbaChannel channel, byte value)
        {
            int i = (int)channel;
            if (color[i] == value)
                return;
            color[i] = value;
            colorHdr[i] = value / 255f;
            if (channel != RgbaChannel.A)
                colorHdr[i] *= Mathf.Pow(2f, exposureValue);
            hdrBaseColor = ExposureAdjustedColor;
            Color.RGBToHSV(BaseColor, out hsv[0], out hsv[1], out hsv[2]);
        }

        public void SetChannelNormalized(RgbaChannel channel, float value) =>
            SetChannel(channel, (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f));

        public float GetChannelHdr(RgbaChannel channel) => colorHdr[(int)channel];

        public void SetChannelHdr(RgbaChannel channel, float value)
        {
            int i = (int)channel;
            if (Mathf.Approximately(colorHdr[i], value))
                return;
            colorHdr[i] = value;
            hdrBaseColor = ExposureAdjustedColor;
            OnHdrChannelChanged(i);
            baseExposureValue = exposureValue;
        }

        public float GetChannel(HsvChannel channel) => hsv[(int)channel];

        public void SetChannel(HsvChannel channel, float value)
        {
            hsv[(int)channel] = Mathf.Clamp01(value);
            Color rgb = Color.HSVToRGB(hsv[0], hsv[1], hsv[2]);
            color[0] = (byte)Mathf.CeilToInt(rgb.r * 255f);
            color[1] = (byte)Mathf.CeilToInt(rgb.g * 255f);
            color[2] = (byte)Mathf.CeilToInt(rgb.b * 255f);
            rgb *= Mathf.Pow(2f, exposureValue);
            colorHdr[0] = rgb.r;
            colorHdr[1] = rgb.g;
            colorHdr[2] = rgb.b;
            hdrBaseColor = ExposureAdjustedColor;
        }

        /// <summary>Sets all four channels from a (possibly HDR) color.</summary>
        public void SetColor(Color c)
        {
            SetChannelHdr(RgbaChannel.R, c.r);
            SetChannelHdr(RgbaChannel.G, c.g);
            SetChannelHdr(RgbaChannel.B, c.b);
            SetChannelHdr(RgbaChannel.A, c.a);
        }

        /// <summary>
        /// Splits an HDR color into an 8-bit base color and an exposure in stops. Colors inside
        /// [1/255, 1] keep exposure 0; anything brighter (or darker) is renormalised so its
        /// brightest channel sits at <see cref="MaxByteForOverexposedColor"/>.
        /// </summary>
        public static void DecomposeHdrColor(
            Color linearHdr,
            out Color32 baseColor,
            out float exposure
        )
        {
            baseColor = linearHdr;
            float max = linearHdr.maxColorComponent;
            if (max == 0f || (max <= 1f && max >= 1f / 255f))
            {
                exposure = 0f;
                baseColor.r = (byte)Mathf.RoundToInt(linearHdr.r * 255f);
                baseColor.g = (byte)Mathf.RoundToInt(linearHdr.g * 255f);
                baseColor.b = (byte)Mathf.RoundToInt(linearHdr.b * 255f);
            }
            else
            {
                float scale = MaxByteForOverexposedColor / max;
                exposure = Mathf.Log(255f / scale) / Mathf.Log(2f);
                baseColor.r = ScaledByte(scale * linearHdr.r);
                baseColor.g = ScaledByte(scale * linearHdr.g);
                baseColor.b = ScaledByte(scale * linearHdr.b);
            }
        }

        private static byte ScaledByte(float value) =>
            (byte)Mathf.Min(MaxByteForOverexposedColor, Mathf.Max(0, Mathf.CeilToInt(value)));

        private void OnHdrChannelChanged(int channel)
        {
            color[3] = (byte)Mathf.RoundToInt(Mathf.Clamp01(colorHdr[3]) * 255f);
            if (channel == (int)RgbaChannel.A)
                return;
            DecomposeHdrColor(ExposureAdjustedColor, out Color32 baseColor, out exposureValue);
            color[0] = baseColor.r;
            color[1] = baseColor.g;
            color[2] = baseColor.b;
            Color.RGBToHSV(BaseColor, out hsv[0], out hsv[1], out hsv[2]);
        }

        private static float ClampFinite(float value)
        {
            if (float.IsPositiveInfinity(value) || float.IsNaN(value))
                return float.MaxValue;
            if (float.IsNegativeInfinity(value))
                return float.MinValue;
            return value;
        }
    }
}
