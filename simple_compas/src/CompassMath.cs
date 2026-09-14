using UnityEngine;

namespace SimpleCompass
{
    internal static class CompassMath
    {
        // Degrees clockwise from north (+z), same convention as a transform's eulerAngles.y.
        public static float Bearing(float dx, float dz) => Mathf.Repeat(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg, 360f);

        // -1..1 across the bar for a bearing seen from a heading; |result| > 1 means off the bar.
        public static float BarPosition(float heading, float bearing, float fov) => Mathf.DeltaAngle(heading, bearing) / (fov * 0.5f);

        // 1 in the middle of the bar, fading to 0 at the ends.
        public static float EdgeFade(float barPos) => 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, Mathf.Abs(barPos)));

        public static string FormatDistance(float meters) =>
            meters < 1000f ? $"{Mathf.RoundToInt(meters)} m" : (meters / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";
    }
}
