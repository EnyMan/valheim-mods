using UnityEngine;
using UnityEngine.UI;

namespace FriendlyClock
{
    // Procedural segmented day/night dial: sunrise at the top, noon right, sunset bottom, midnight left.
    // Face is static (rebuilt only on resize/edit toggle); Hand is a separate graphic rotated by its transform.
    public sealed class DialGraphic : MaskableGraphic
    {
        public enum Part { Face, Hand, Cap }

        public Part part;
        private bool _editMode;
        private int _segments = 12;
        private Texture2D _texture;

        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        // null = draw procedurally. Takes ownership of the texture.
        public void SetTexture(Texture2D texture)
        {
            if (_texture != null) Destroy(_texture);
            _texture = texture;
            SetAllDirty();
        }

        private static readonly Vector2 LightDir = new Vector2(-0.6f, 0.8f);
        private static readonly Color32 Shadow = new Color32(28, 20, 14, 255);
        private static readonly Color32 BronzeDark = new Color32(92, 62, 32, 255);
        private static readonly Color32 BronzeLight = new Color32(196, 146, 84, 255);
        private static readonly Color32 Cream = new Color32(240, 226, 180, 255);

        public void SetEditMode(bool enabled)
        {
            if (_editMode == enabled) return;
            _editMode = enabled;
            SetVerticesDirty();
        }

        public void SetSegments(int count)
        {
            count = Mathf.Clamp(count, 2, 48);
            if (_segments == count) return;
            _segments = count;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            if (r <= 0f) return;
            if (_texture != null) Textured(vh, r);
            else if (part == Part.Hand) Hand(vh, r);
            else if (part == Part.Cap) Cap(vh, r);
            else Face(vh, r);
        }

        private void Face(VertexHelper vh, float r)
        {
            var boost = _editMode ? 1.25f : 1f;
            Ring(vh, r * 1.02f, 0f, new Color32(0, 0, 0, 90), new Color32(0, 0, 0, 90), new Vector2(2f, -2.5f)); // drop shadow
            Ring(vh, r, r * 0.965f, Shadow, Shadow, Vector2.zero);
            Ring(vh, r * 0.965f, r * 0.84f, Tint(BronzeDark, boost), Tint(BronzeLight, boost), Vector2.zero);
            Ring(vh, r * 0.84f, r * 0.815f, Tint(BronzeLight, boost), Tint(new Color32(236, 196, 128, 255), boost), Vector2.zero);
            Ring(vh, r * 0.815f, 0f, new Color32(18, 18, 24, 255), new Color32(18, 18, 24, 255), Vector2.zero);

            // One wedge per day phase; phase i covers day fraction [i/n, (i+1)/n) from midnight. Sunrise (0.25) is at the top.
            var faceR = r * 0.79f;
            const float gap = 0.0022f;
            for (var i = 0; i < _segments; i++)
            {
                var mid = (i + 0.5f) / _segments;
                var day = mid > 0.25f && mid < 0.75f;
                var t = day ? (mid - 0.25f) / 0.5f : Mathf.Repeat(mid - 0.75f, 1f) / 0.5f;
                var c = day
                    ? Color.Lerp(new Color32(246, 208, 92, 255), new Color32(204, 90, 56, 255), t * t)
                    : Color.Lerp(new Color32(58, 84, 118, 255), new Color32(26, 38, 68, 255), t);
                c *= i % 2 == 0 ? 1f : 0.92f;
                c.a = 1f;
                float from = (float)i / _segments - 0.25f, to = (float)(i + 1) / _segments - 0.25f;
                float a = Angle(from + gap), b = Angle(to - gap);
                Quad(vh, Point(r * 0.06f, a), Point(faceR, a), Point(faceR, b), Point(r * 0.06f, b), c, c);
            }

            // Night sky on the left half.
            Crescent(vh, new Vector2(-0.48f, -0.1f) * r, r * 0.13f, Cream);
            Star(vh, new Vector2(-0.40f, 0.36f) * r, r * 0.055f, Cream);
            Star(vh, new Vector2(-0.20f, 0.52f) * r, r * 0.035f, Cream);
            Star(vh, new Vector2(-0.22f, 0.24f) * r, r * 0.03f, Cream);
            Star(vh, new Vector2(-0.60f, 0.20f) * r, r * 0.03f, Cream);

            for (var j = 0; j < 4; j++) Stud(vh, r, j / 4f, boost);
        }

        // Painted layers share one square canvas centred on the dial, so they stack without offsets.
        private void Textured(VertexHelper vh, float r)
        {
            var c = (Color32)color;
            vh.AddVert(new Vector3(-r, -r), c, new Vector4(0f, 0f));
            vh.AddVert(new Vector3(-r, r), c, new Vector4(0f, 1f));
            vh.AddVert(new Vector3(r, r), c, new Vector4(1f, 1f));
            vh.AddVert(new Vector3(r, -r), c, new Vector4(1f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        private static void Hand(VertexHelper vh, float r)
        {
            var shadow = new Vector2(1.5f, -1.5f);
            var dark = new Color32(20, 16, 12, 200);
            Arrow(vh, r, shadow, dark);
            Arrow(vh, r, Vector2.zero, Cream);
        }

        private static void Cap(VertexHelper vh, float r)
        {
            Disc(vh, r * 0.13f, new Vector2(1.5f, -1.5f), new Color32(20, 16, 12, 200));
            Disc(vh, r * 0.12f, Vector2.zero, Shadow);
            Disc(vh, r * 0.10f, Vector2.zero, BronzeLight);
            Disc(vh, r * 0.04f, Vector2.zero, Shadow);
        }

        private static void Arrow(VertexHelper vh, float r, Vector2 o, Color32 c)
        {
            var w = r * 0.028f;
            Quad(vh, o + new Vector2(-w, -r * 0.10f), o + new Vector2(-w, r * 0.54f), o + new Vector2(w, r * 0.54f), o + new Vector2(w, -r * 0.10f), c, c);
            Tri(vh, o + new Vector2(-r * 0.085f, r * 0.50f), o + new Vector2(0f, r * 0.74f), o + new Vector2(r * 0.085f, r * 0.50f), c);
        }

        // Two-tone pyramid stud on the rim, pointing at the centre.
        private static void Stud(VertexHelper vh, float r, float at, float boost)
        {
            Vector2 tip = OnClock(r * 0.80f, at), left = OnClock(r * 1.03f, at - 0.028f), right = OnClock(r * 1.03f, at + 0.028f), mid = OnClock(r * 1.03f, at);
            Tri(vh, OnClock(r * 0.78f, at), OnClock(r * 1.06f, at - 0.032f), OnClock(r * 1.06f, at + 0.032f), Shadow);
            Tri(vh, tip, left, mid, Tint(BronzeLight, boost));
            Tri(vh, tip, mid, right, Tint(BronzeDark, boost));
        }

        private static void Crescent(VertexHelper vh, Vector2 centre, float radius, Color32 c)
        {
            const int steps = 16;
            for (var i = 0; i < steps; i++)
            {
                // Outer edge is the left half circle, inner edge a squashed half circle: opens to the right.
                float a0 = Mathf.PI * (0.5f + (float)i / steps), a1 = Mathf.PI * (0.5f + (float)(i + 1) / steps);
                Vector2 o0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, o1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                Vector2 i0 = new Vector2(o0.x * 0.35f, o0.y), i1 = new Vector2(o1.x * 0.35f, o1.y);
                Quad(vh, centre + i0, centre + o0, centre + o1, centre + i1, c, c);
            }
        }

        private static void Star(VertexHelper vh, Vector2 centre, float size, Color32 c)
        {
            var thin = size * 0.22f;
            Diamond(vh, centre, thin, size, c);
            Diamond(vh, centre, size, thin, c);
        }

        private static void Diamond(VertexHelper vh, Vector2 centre, float halfW, float halfH, Color32 c)
        {
            Tri(vh, centre + new Vector2(0f, halfH), centre + new Vector2(halfW, 0f), centre + new Vector2(-halfW, 0f), c);
            Tri(vh, centre + new Vector2(0f, -halfH), centre + new Vector2(-halfW, 0f), centre + new Vector2(halfW, 0f), c);
        }

        // Ring shaded by angle as if lit from the top-left (gives the bronze a metallic bevel).
        private static void Ring(VertexHelper vh, float outer, float inner, Color32 dark, Color32 light, Vector2 o)
        {
            const int segments = 96;
            for (var i = 0; i < segments; i++)
            {
                float a = Angle((float)i / segments), b = Angle((float)(i + 1) / segments);
                Color32 ca = Lit(dark, light, a), cb = Lit(dark, light, b);
                Quad(vh, Point(inner, a) + o, Point(outer, a) + o, Point(outer, b) + o, Point(inner, b) + o, ca, cb);
            }
        }

        private static Color32 Lit(Color32 dark, Color32 light, float angle) =>
            Color32.Lerp(dark, light, 0.5f + 0.5f * Vector2.Dot(Point(1f, angle), LightDir));

        private static Color32 Tint(Color32 c, float k) =>
            new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), c.a);

        private static void Disc(VertexHelper vh, float radius, Vector2 o, Color32 c) => Ring(vh, radius, 0f, c, c, o);

        private static float Angle(float fraction) => Mathf.PI / 2f - fraction * Mathf.PI * 2f;
        private static Vector2 Point(float radius, float angle) => new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
        private static Vector2 OnClock(float radius, float fraction) => Point(radius, Angle(fraction));

        private static void Tri(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            var n = vh.currentVertCount;
            vh.AddVert(a, color, Vector4.zero);
            vh.AddVert(b, color, Vector4.zero);
            vh.AddVert(c, color, Vector4.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }

        // a,b share colour ca; c,d share cb.
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ca, Color32 cb)
        {
            var n = vh.currentVertCount;
            vh.AddVert(a, ca, Vector4.zero);
            vh.AddVert(b, ca, Vector4.zero);
            vh.AddVert(c, cb, Vector4.zero);
            vh.AddVert(d, cb, Vector4.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
