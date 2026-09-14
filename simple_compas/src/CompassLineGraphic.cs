using UnityEngine;
using UnityEngine.UI;

namespace SimpleCompass
{
    // Thin line with faded ends, ticks at cardinals/intercardinals and a centre marker. Rebuilt when the heading changes.
    public sealed class CompassLineGraphic : MaskableGraphic
    {
        private const int LineSegments = 24;
        public float heading;
        public float fov = 160f;
        public bool intercardinalTicks = true;
        public float gapCenter; // bar units; the line is cut out behind the pin label
        public float gapHalfWidth;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var half = rectTransform.rect.width * 0.5f;
            if (half <= 0f) return;
            var c = (Color32)color;

            for (var i = 0; i < LineSegments; i++)
            {
                float x0 = Mathf.Lerp(-half, half, (float)i / LineSegments), x1 = Mathf.Lerp(-half, half, (float)(i + 1) / LineSegments);
                LinePiece(vh, x0, Mathf.Min(x1, gapCenter - gapHalfWidth), half, c);
                LinePiece(vh, Mathf.Max(x0, gapCenter + gapHalfWidth), x1, half, c);
            }

            for (var b = 0; b < 360; b += 45)
            {
                var cardinal = b % 90 == 0;
                if (!cardinal && !intercardinalTicks) continue;
                var pos = CompassMath.BarPosition(heading, b, fov);
                if (Mathf.Abs(pos) > 1f) continue;
                var x = pos * half;
                if (Mathf.Abs(x - gapCenter) < gapHalfWidth) continue;
                var h = cardinal ? 7f : 4f;
                var tc = Alpha(c, CompassMath.EdgeFade(pos));
                Quad(vh, new Vector2(x - 1f, -1f - h), new Vector2(x - 1f, -1f), new Vector2(x + 1f, -1f), new Vector2(x + 1f, -1f - h), tc, tc); // below the line, icons live above
            }

            // Centre marker: small triangle under the line pointing up at the heading.
            var n = vh.currentVertCount;
            vh.AddVert(new Vector3(0f, -2f), c, Vector4.zero);
            vh.AddVert(new Vector3(5f, -9f), c, Vector4.zero);
            vh.AddVert(new Vector3(-5f, -9f), c, Vector4.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }

        private static void LinePiece(VertexHelper vh, float x0, float x1, float half, Color32 c)
        {
            if (x1 <= x0) return;
            Quad(vh, new Vector2(x0, -1f), new Vector2(x0, 1f), new Vector2(x1, 1f), new Vector2(x1, -1f),
                Alpha(c, CompassMath.EdgeFade(x0 / half)), Alpha(c, CompassMath.EdgeFade(x1 / half)));
        }

        private static Color32 Alpha(Color32 c, float a) => new Color32(c.r, c.g, c.b, (byte)(c.a * a));

        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 left, Color32 right)
        {
            var n = vh.currentVertCount;
            vh.AddVert(a, left, Vector4.zero);
            vh.AddVert(b, left, Vector4.zero);
            vh.AddVert(c, right, Vector4.zero);
            vh.AddVert(d, right, Vector4.zero);
            vh.AddTriangle(n, n + 1, n + 2);
            vh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
