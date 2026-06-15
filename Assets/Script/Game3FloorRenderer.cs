using System.Collections.Generic;
using UnityEngine;

// Attach this to the Game3 floor GameObject (same one carrying the SpriteRenderer or Renderer).
// Call Initialize() with the colored city image at game start.
// The floor begins fully colored; holes are animated grayscale patches that spread from a center point.
[AddComponentMenu("TD/Achroma/Game 3 Floor Renderer")]
[RequireComponent(typeof(Renderer))]
public class Game3FloorRenderer : MonoBehaviour
{
    [Tooltip("Output texture resolution. 512 gives good quality at an acceptable per-frame CPU cost.")]
    public int resolution = 512;

    [Tooltip("Seconds for hole desaturation to spread from its center to its edge.")]
    public float holeSpreadDuration = 0.45f;

    [Tooltip("Seconds for color to flood back after a hole is repaired.")]
    public float repairFloodDuration = 0.35f;

    [Tooltip("Brightness oscillation amplitude on fully-gray hole areas. Creates a ghostly flicker.")]
    [Range(0f, 0.30f)]
    public float holePulseMagnitude = 0.13f;

    [Tooltip("Brightness of fully-formed holes relative to the original image (0 = pure black, 1 = original brightness). Lower = more visible holes.")]
    [Range(0f, 1f)]
    public float holeBrightness = 0.25f;

    [Tooltip("Extra brightness boost at the spreading crack edge. Higher = brighter/more visible crack front.")]
    [Range(0f, 3f)]
    public float crackEdgeBrightness = 1.4f;

    // ── Private ──────────────────────────────────────────────────────────────────
    private Renderer       _renderer;
    private SpriteRenderer _spriteRenderer;
    private float          _spritePixelsPerUnit  = 100f;
    private Vector2        _originalSpriteLocalSize;
    private Vector2        _originalSpritePivot  = new Vector2(0.5f, 0.5f);
    private Vector3        _originalLocalScale;

    private Texture2D _outputTexture;
    private Color32[] _outputPixels;
    private Color32[] _colorPixels;
    private Color32[] _grayPixels;
    private float[]   _blendMask;   // 0 = full color, 1 = full gray
    private float[]   _targetMask;
    private float[]   _animSpeed;

    private bool  _initialized = false;
    private float _pulseTime   = 0f;

    private sealed class HoleEntry
    {
        public int    id;
        public Vector2 uv;
        public float  uvRadius;
        public bool   done;
    }
    private readonly List<HoleEntry> _holes  = new List<HoleEntry>();
    private int                      _nextId = 0;

    // ── Lifecycle ─────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _renderer       = GetComponent<Renderer>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalLocalScale = transform.localScale;
        if (_spriteRenderer != null && _spriteRenderer.sprite != null)
        {
            var s = _spriteRenderer.sprite;
            _spritePixelsPerUnit     = s.pixelsPerUnit;
            _originalSpriteLocalSize = s.bounds.size;
            _originalSpritePivot     = new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height);
        }
    }

    private void LateUpdate()
    {
        if (!_initialized) return;

        _pulseTime += Time.deltaTime;
        bool anyAnim = false;

        for (int i = 0; i < _blendMask.Length; i++)
        {
            float diff = _targetMask[i] - _blendMask[i];
            if (Mathf.Abs(diff) < 0.001f) continue;
            _blendMask[i] += Mathf.Sign(diff) * _animSpeed[i] * Time.deltaTime;
            _blendMask[i]  = Mathf.Clamp01(_blendMask[i]);
            anyAnim = true;
        }

        bool pulsing = holePulseMagnitude > 0f && _holes.Count > 0;
        if (!anyAnim && !pulsing) return;

        float pulse = pulsing
            ? 1f + holePulseMagnitude * Mathf.Sin(_pulseTime * Mathf.PI * 2f)
            : 1f;

        for (int i = 0; i < _outputPixels.Length; i++)
        {
            float t = _blendMask[i];
            if (t < 0.001f) { _outputPixels[i] = _colorPixels[i]; continue; }

            float frontGlow = 0f;
            if (t > 0.05f && t < 0.80f)
                frontGlow = Mathf.Sin((t - 0.05f) / 0.75f * Mathf.PI) * crackEdgeBrightness;

            float brt = pulse * (1f + frontGlow);

            if (t > 0.999f)
            {
                byte g = (byte)Mathf.Clamp(_grayPixels[i].r * brt, 0f, 255f);
                _outputPixels[i] = new Color32(g, g, g, _colorPixels[i].a);
            }
            else
            {
                byte r = (byte)Mathf.Clamp(Mathf.Lerp(_colorPixels[i].r, _grayPixels[i].r, t) * brt, 0f, 255f);
                byte g = (byte)Mathf.Clamp(Mathf.Lerp(_colorPixels[i].g, _grayPixels[i].g, t) * brt, 0f, 255f);
                byte b = (byte)Mathf.Clamp(Mathf.Lerp(_colorPixels[i].b, _grayPixels[i].b, t) * brt, 0f, 255f);
                _outputPixels[i] = new Color32(r, g, b, _colorPixels[i].a);
            }
        }

        _outputTexture.SetPixels32(_outputPixels);
        _outputTexture.Apply(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────────

    // Call this at Game 3 start. coloredCityImage is the Game 2 colored wall image.
    public void Initialize(Texture2D coloredCityImage)
    {
        _initialized = false;
        _holes.Clear();
        _nextId    = 0;
        _pulseTime = 0f;

        int res      = resolution;
        _colorPixels = SampleTexture(coloredCityImage, res);
        _grayPixels  = new Color32[res * res];

        for (int i = 0; i < _colorPixels.Length; i++)
        {
            float lum  = 0.299f * _colorPixels[i].r + 0.587f * _colorPixels[i].g + 0.114f * _colorPixels[i].b;
            byte  dark = (byte)(lum * holeBrightness);
            _grayPixels[i] = new Color32(dark, dark, dark, _colorPixels[i].a);
        }

        _blendMask    = new float[res * res];
        _targetMask   = new float[res * res];
        _animSpeed    = new float[res * res];
        _outputPixels = (Color32[])_colorPixels.Clone();

        if (_outputTexture == null || _outputTexture.width != res)
        {
            if (_outputTexture != null) Destroy(_outputTexture);
            _outputTexture = new Texture2D(res, res, TextureFormat.RGBA32, false)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }

        _outputTexture.SetPixels32(_outputPixels);
        _outputTexture.Apply(false);
        AssignOutputTexture();
        _initialized = true;
    }

    // Animates a grayscale hole spreading from the given UV position.
    // Returns a hole ID to pass to RepairHole() later.
    public int SpawnHole(Vector2 uv, float uvRadius)
    {
        int   id  = _nextId++;
        float spd = 1f / Mathf.Max(0.05f, holeSpreadDuration);
        int   res = resolution;
        int   cx  = Mathf.Clamp(Mathf.RoundToInt(uv.x * (res - 1)), 0, res - 1);
        int   cy  = Mathf.Clamp(Mathf.RoundToInt(uv.y * (res - 1)), 0, res - 1);

        int prX, prY;
        GetEllipseRadii(uvRadius, out prX, out prY);
        int prMax = Mathf.Max(prX, prY);

        for (int dy = -prMax; dy <= prMax; dy++)
        {
            int py = cy + dy; if (py < 0 || py >= res) continue;
            for (int dx = -prMax; dx <= prMax; dx++)
            {
                float ex = (float)dx / prX, ey = (float)dy / prY;
                if (ex * ex + ey * ey > 1f) continue;
                int px = cx + dx; if (px < 0 || px >= res) continue;
                int   idx  = py * res + px;
                float norm = Mathf.Sqrt(ex * ex + ey * ey); // 0 at center, 1 at world-space edge
                float ls   = spd * Mathf.Lerp(2.1f, 0.65f, norm);
                _targetMask[idx] = 1f;
                if (ls > _animSpeed[idx]) _animSpeed[idx] = ls; // honour overlapping holes
            }
        }

        _holes.Add(new HoleEntry { id = id, uv = uv, uvRadius = uvRadius });
        return id;
    }

    // Triggers the color-flood-back animation. Inner pixels restore first (from-center spreading).
    // Pixels that overlap with another surviving hole are left gray.
    public void RepairHole(int holeId)
    {
        HoleEntry e = null;
        for (int i = 0; i < _holes.Count; i++) if (_holes[i].id == holeId) { e = _holes[i]; break; }
        if (e == null || e.done) return;

        e.done = true;
        _holes.Remove(e);  // remove first so it's excluded from the overlap check below

        float spd = 1f / Mathf.Max(0.05f, repairFloodDuration);
        int   res = resolution;
        int   cx  = Mathf.Clamp(Mathf.RoundToInt(e.uv.x * (res - 1)), 0, res - 1);
        int   cy  = Mathf.Clamp(Mathf.RoundToInt(e.uv.y * (res - 1)), 0, res - 1);

        int prX, prY;
        GetEllipseRadii(e.uvRadius, out prX, out prY);
        int prMax = Mathf.Max(prX, prY);

        for (int dy = -prMax; dy <= prMax; dy++)
        {
            int py = cy + dy; if (py < 0 || py >= res) continue;
            for (int dx = -prMax; dx <= prMax; dx++)
            {
                float ex = (float)dx / prX, ey = (float)dy / prY;
                if (ex * ex + ey * ey > 1f) continue;
                int px = cx + dx; if (px < 0 || px >= res) continue;
                int idx = py * res + px;

                // Skip pixels still covered by another hole to avoid the "bitten" look
                bool covered = false;
                for (int h = 0; h < _holes.Count; h++)
                {
                    var o    = _holes[h];
                    int ocx  = Mathf.Clamp(Mathf.RoundToInt(o.uv.x * (res - 1)), 0, res - 1);
                    int ocy  = Mathf.Clamp(Mathf.RoundToInt(o.uv.y * (res - 1)), 0, res - 1);
                    int oprX, oprY;
                    GetEllipseRadii(o.uvRadius, out oprX, out oprY);
                    float oex = (float)(px - ocx) / oprX, oey = (float)(py - ocy) / oprY;
                    if (oex * oex + oey * oey <= 1f) { covered = true; break; }
                }
                if (covered) continue;

                float norm = Mathf.Sqrt(ex * ex + ey * ey);
                float ls   = spd * Mathf.Lerp(2.8f, 0.75f, norm);
                _targetMask[idx] = 0f;
                _animSpeed[idx]  = ls;
            }
        }
    }

    public void ClearAllHoles()
    {
        _holes.Clear();
        for (int i = 0; i < _targetMask.Length; i++) { _targetMask[i] = 0f; _animSpeed[i] = 5f; }
    }

    // Desaturates pixels in the annular band [uvRadiusInner, uvRadiusOuter] around uvCenter.
    // Call each frame during burst ring expansion with the previous and current UV radius.
    public void DesaturateRingBand(Vector2 uvCenter, float uvRadiusInner, float uvRadiusOuter, float animSpeed = 20f)
    {
        if (!_initialized) return;
        int res   = resolution;
        int cx    = Mathf.Clamp(Mathf.RoundToInt(uvCenter.x * (res - 1)), 0, res - 1);
        int cy    = Mathf.Clamp(Mathf.RoundToInt(uvCenter.y * (res - 1)), 0, res - 1);
        int prIn  = Mathf.RoundToInt(uvRadiusInner * res);
        int prOut = Mathf.Max(Mathf.RoundToInt(uvRadiusOuter * res), prIn + 1);

        int xLo = Mathf.Max(0,       cx - prOut);
        int xHi = Mathf.Min(res - 1, cx + prOut);
        int yLo = Mathf.Max(0,       cy - prOut);
        int yHi = Mathf.Min(res - 1, cy + prOut);

        long in2  = (long)prIn  * prIn;
        long out2 = (long)prOut * prOut;

        for (int py = yLo; py <= yHi; py++)
        {
            long dy  = py - cy;
            long dy2 = dy * dy;
            if (dy2 > out2) continue;
            for (int px = xLo; px <= xHi; px++)
            {
                long dx = px - cx;
                long d2 = dx * dx + dy2;
                if (d2 > out2 || d2 < in2) continue;
                int idx = py * res + px;
                if (_targetMask[idx] >= 1f) continue;
                _targetMask[idx] = 1f;
                if (animSpeed > _animSpeed[idx]) _animSpeed[idx] = animSpeed;
            }
        }
    }

    // Desaturates every pixel on the floor — call after burst ring finishes to cover stragglers.
    public void DesaturateAll(float animSpeed = 20f)
    {
        if (!_initialized) return;
        for (int i = 0; i < _targetMask.Length; i++)
        {
            if (_targetMask[i] >= 1f) continue;
            _targetMask[i] = 1f;
            if (animSpeed > _animSpeed[i]) _animSpeed[i] = animSpeed;
        }
    }

    // Restores color (targetMask → 0) for pixel rows yPixelFrom..yPixelTo inclusive.
    // Call each frame during energy sweep with an increasing row range (bottom to top).
    public void RestoreColorRows(int yPixelFrom, int yPixelTo, float animSpeed = 5f)
    {
        if (!_initialized) return;
        int res = resolution;
        int y0  = Mathf.Clamp(yPixelFrom, 0, res - 1);
        int y1  = Mathf.Clamp(yPixelTo,   0, res - 1);
        for (int y = y0; y <= y1; y++)
        for (int x = 0; x < res; x++)
        {
            int idx = y * res + x;
            if (_targetMask[idx] <= 0f) continue;
            _targetMask[idx] = 0f;
            _animSpeed[idx]  = animSpeed;
        }
    }

    // ── Coordinate Utilities (same API as ColoringFloorRenderer) ─────────────────

    public bool TryGetCanvasBounds(out Vector2 min, out Vector2 max)
    {
        if (_renderer == null) { min = max = Vector2.zero; return false; }
        Bounds b    = _renderer.bounds;
        bool   flat = b.size.z >= b.size.y;
        min = flat ? new Vector2(b.min.x, b.min.z) : new Vector2(b.min.x, b.min.y);
        max = flat ? new Vector2(b.max.x, b.max.z) : new Vector2(b.max.x, b.max.y);
        return true;
    }

    public bool IsInsideCanvas(Vector2 wp)
    {
        Bounds b    = _renderer.bounds;
        bool   flat = b.size.z >= b.size.y;
        return flat
            ? wp.x >= b.min.x && wp.x <= b.max.x && wp.y >= b.min.z && wp.y <= b.max.z
            : wp.x >= b.min.x && wp.x <= b.max.x && wp.y >= b.min.y && wp.y <= b.max.y;
    }

    public Vector2 WorldToUV(Vector2 wp)
    {
        Bounds b    = _renderer.bounds;
        bool   flat = b.size.z >= b.size.y;
        return flat
            ? new Vector2(Mathf.InverseLerp(b.min.x, b.max.x, wp.x), Mathf.InverseLerp(b.min.z, b.max.z, wp.y))
            : new Vector2(Mathf.InverseLerp(b.min.x, b.max.x, wp.x), Mathf.InverseLerp(b.min.y, b.max.y, wp.y));
    }

    // Converts a world-space radius to a UV radius based on the canvas X extent.
    public float WorldRadiusToUV(float worldRadius)
    {
        Bounds b = _renderer.bounds;
        return b.size.x > 0f ? worldRadius / b.size.x : 0.05f;
    }

    // ── Private Helpers ───────────────────────────────────────────────────────────

    // Computes separate pixel radii for X and Y so holes render as world-space circles
    // even when the canvas has a non-square aspect ratio (e.g. 16:9 floor projection).
    private void GetEllipseRadii(float uvRadius, out int prX, out int prY)
    {
        prX = Mathf.Max(1, Mathf.RoundToInt(uvRadius * resolution));
        if (_renderer == null) { prY = prX; return; }
        Bounds b      = _renderer.bounds;
        float canvasW = b.size.x;
        float canvasH = b.size.z >= b.size.y ? b.size.z : b.size.y;
        prY = (canvasW > 0.001f && canvasH > 0.001f)
            ? Mathf.Max(1, Mathf.RoundToInt(uvRadius * resolution * canvasW / canvasH))
            : prX;
    }

    private void AssignOutputTexture()
    {
        if (_spriteRenderer != null)
        {
            _spriteRenderer.sprite = Sprite.Create(
                _outputTexture,
                new Rect(0, 0, _outputTexture.width, _outputTexture.height),
                _originalSpritePivot,
                _spritePixelsPerUnit);

            if (_originalSpriteLocalSize.magnitude > 0.01f)
            {
                Vector2 nls = _spriteRenderer.sprite.bounds.size;
                if (nls.x > 0.001f && nls.y > 0.001f)
                    transform.localScale = new Vector3(
                        _originalLocalScale.x * (_originalSpriteLocalSize.x / nls.x),
                        _originalLocalScale.y * (_originalSpriteLocalSize.y / nls.y),
                        _originalLocalScale.z);
            }
        }
        else
        {
            _renderer.material.mainTexture = _outputTexture;
        }
    }

    private static Color32[] SampleTexture(Texture2D src, int res)
    {
        if (src == null)
        {
            var blank = new Color32[res * res];
            for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(80, 80, 80, 255);
            return blank;
        }
        var rt   = RenderTexture.GetTemporary(res, res, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tmp  = new Texture2D(res, res, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, res, res), 0, 0);
        tmp.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var px = tmp.GetPixels32();
        Destroy(tmp);
        return px;
    }
}
