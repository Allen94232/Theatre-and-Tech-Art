using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("TD/Achroma/Coloring Floor Renderer")]
[RequireComponent(typeof(Renderer))]
public class ColoringFloorRenderer : MonoBehaviour
{
    [Tooltip("Internal paint mask resolution. 1080 = good for HD output. 2160 = 4K quality but heavier (~8ms Apply per frame).")]
    public int maskResolution = 1080;

    private Renderer        _renderer;
    private SpriteRenderer  _spriteRenderer;
    private float           _spritePixelsPerUnit = 100f;
    private Vector2         _originalSpriteLocalSize; // sprite local units at scale=1, recorded before any override
    private Vector2         _originalSpritePivot = new Vector2(0.5f, 0.5f); // normalized pivot preserved across Initialize()
    private Vector3         _originalLocalScale;
    private Texture2D _outputTexture;
    private Color32[] _outputPixels;
    private Color32[] _grayscalePixels;
    private Color32[] _colorPixels;
    // Per-pixel bitmask: 0 = unregioned (all players allowed); bit k set = player slot k allowed.
    // Overlapping regions OR their masks so multiple players share the pixel.
    private int[]  _pixelAllowedMask;
    private bool[] _painted;

    private List<AchromaGame2Region> _regions;
    private int  _totalPaintable;
    private int  _paintedCount;
    private bool _dirty;
    private bool _completed;
    private int  _resolution;

    public float CompletionRatio => _totalPaintable > 0 ? (float)_paintedCount / _totalPaintable : 0f;

    private void Awake()
    {
        _renderer       = GetComponent<Renderer>();
        _spriteRenderer = GetComponent<SpriteRenderer>();
        _originalLocalScale = transform.localScale;
        if (_spriteRenderer != null && _spriteRenderer.sprite != null)
        {
            var s = _spriteRenderer.sprite;
            _spritePixelsPerUnit     = s.pixelsPerUnit;
            _originalSpriteLocalSize = s.bounds.size; // local units at scale=1
            // Preserve the original normalized pivot so AssignOutputTexture() doesn't shift the world-space center.
            _originalSpritePivot = new Vector2(s.pivot.x / s.rect.width, s.pivot.y / s.rect.height);
        }
    }

    public void Initialize(AchromaGame2Level level)
    {
        _regions      = level.regions;
        _completed    = false;
        _paintedCount = 0;
        _dirty        = false;
        _resolution   = maskResolution;

        int res = _resolution;

        _grayscalePixels = SampleTextureToArray(level.grayscaleImage, res);
        _colorPixels     = SampleTextureToArray(level.coloredImage,   res);

        // Build per-pixel allowed-player bitmask by testing each UV against all regions.
        // Overlapping regions OR their slots so both players can paint the shared area.
        // mask == 0: pixel belongs to no region — any player may paint it; still counts toward completion.
        // This runs once per level load; the one-time cost is acceptable.
        _pixelAllowedMask = new int[res * res];
        _painted          = new bool[res * res];
        _totalPaintable   = res * res; // every pixel counts toward completion

        for (int y = 0; y < res; y++)
        {
            float v = (float)y / (res - 1);
            for (int x = 0; x < res; x++)
            {
                float u    = (float)x / (res - 1);
                int   mask = BuildAllowedMask(new Vector2(u, v));
                _pixelAllowedMask[y * res + x] = mask;
            }
        }

        _outputPixels = (Color32[])_grayscalePixels.Clone();

        if (_outputTexture == null || _outputTexture.width != res || _outputTexture.height != res)
        {
            if (_outputTexture != null) Destroy(_outputTexture);
            _outputTexture            = new Texture2D(res, res, TextureFormat.RGBA32, false);
            _outputTexture.filterMode = FilterMode.Bilinear;
            _outputTexture.wrapMode   = TextureWrapMode.Clamp;
        }

        _outputTexture.SetPixels32(_outputPixels);
        _outputTexture.Apply();
        AssignOutputTexture();
    }

    // Assigns the output texture to the renderer, handling SpriteRenderer separately.
    private void AssignOutputTexture()
    {
        if (_spriteRenderer != null)
        {
            // Use the original sprite's pivot so the world-space bounds center stays fixed.
            // Hardcoding (0.5, 0.5) here would shift the displayed area relative to the receiver's cached bounds.
            _spriteRenderer.sprite = Sprite.Create(
                _outputTexture,
                new Rect(0, 0, _outputTexture.width, _outputTexture.height),
                _originalSpritePivot,
                _spritePixelsPerUnit);

            // The new sprite may have different local dimensions than the original (e.g. 1:1 mask vs 16:9 source).
            // Compensate with scale so the rendered world size stays exactly what the user set in the editor.
            if (_originalSpriteLocalSize.magnitude > 0.01f)
            {
                Vector2 newLocalSize = _spriteRenderer.sprite.bounds.size;
                if (newLocalSize.x > 0.001f && newLocalSize.y > 0.001f)
                {
                    transform.localScale = new Vector3(
                        _originalLocalScale.x * (_originalSpriteLocalSize.x / newLocalSize.x),
                        _originalLocalScale.y * (_originalSpriteLocalSize.y / newLocalSize.y),
                        _originalLocalScale.z);
                }
            }
        }
        else
        {
            _renderer.material.mainTexture = _outputTexture;
        }
    }

    // Returns the canvas renderer's world-space 2D bounds (XY or XZ depending on orientation).
    public bool TryGetCanvasBounds(out Vector2 min, out Vector2 max)
    {
        if (_renderer == null) { min = max = Vector2.zero; return false; }
        Bounds b = _renderer.bounds;
        bool flatFloor = b.size.z >= b.size.y;
        if (flatFloor)
        {
            min = new Vector2(b.min.x, b.min.z);
            max = new Vector2(b.max.x, b.max.z);
        }
        else
        {
            min = new Vector2(b.min.x, b.min.y);
            max = new Vector2(b.max.x, b.max.y);
        }
        return true;
    }

    // Returns true when worldPos2D falls within the canvas renderer's world-space footprint.
    // Call this before WorldToUV — positions outside the canvas clamp to the border in InverseLerp,
    // which would incorrectly paint at the canvas edge instead of doing nothing.
    public bool IsInsideCanvas(Vector2 worldPos2D)
    {
        Bounds b = _renderer.bounds;
        bool flatFloor = b.size.z >= b.size.y;
        if (flatFloor)
            return worldPos2D.x >= b.min.x && worldPos2D.x <= b.max.x &&
                   worldPos2D.y >= b.min.z && worldPos2D.y <= b.max.z;
        return worldPos2D.x >= b.min.x && worldPos2D.x <= b.max.x &&
               worldPos2D.y >= b.min.y && worldPos2D.y <= b.max.y;
    }

    // Convert a 2D arena world position (XY or XZ depending on floor orientation) to texture UV [0,1]^2.
    // Uses the floor renderer's current world-space bounds so the result always matches the display,
    // even if the bounds changed after TDTableReceiver's Awake() cached them.
    public Vector2 WorldToUV(Vector2 worldPos2D)
    {
        Bounds b = _renderer.bounds;
        bool flatFloor = b.size.z >= b.size.y;
        if (flatFloor)
        {
            return new Vector2(
                Mathf.InverseLerp(b.min.x, b.max.x, worldPos2D.x),
                Mathf.InverseLerp(b.min.z, b.max.z, worldPos2D.y));
        }
        return new Vector2(
            Mathf.InverseLerp(b.min.x, b.max.x, worldPos2D.x),
            Mathf.InverseLerp(b.min.y, b.max.y, worldPos2D.y));
    }

    // Converts a world-unit radius to per-axis UV radii, accounting for non-square arenas.
    // Using only one axis (e.g. arenaSize.x) gives an elliptical brush when width != height.
    public void GetPaintRadii(float worldRadius, out float uvRadiusX, out float uvRadiusY)
    {
        Bounds b = _renderer.bounds;
        bool flatFloor = b.size.z >= b.size.y;
        float sizeX = b.size.x;
        float sizeY = flatFloor ? b.size.z : b.size.y;
        uvRadiusX = sizeX > 0f ? worldRadius / sizeX : 0.05f;
        uvRadiusY = sizeY > 0f ? worldRadius / sizeY : 0.05f;
    }

    // uv: normalised [0,1]^2 player position; radiusX/Y: brush radius as fraction of texture dimension per axis.
    // Using separate X/Y radii makes the brush circular in world space even on non-square arenas.
    public void Paint(Vector2 uv, int playerSlot, float radiusX, float radiusY)
    {
        if (_completed || _pixelAllowedMask == null) return;

        int playerBit = 1 << playerSlot;
        int res = _resolution;
        int cx  = Mathf.Clamp(Mathf.RoundToInt(uv.x * (res - 1)), 0, res - 1);
        int cy  = Mathf.Clamp(Mathf.RoundToInt(uv.y * (res - 1)), 0, res - 1);
        int prX = Mathf.Max(1, Mathf.RoundToInt(radiusX * res));
        int prY = Mathf.Max(1, Mathf.RoundToInt(radiusY * res));
        float invPrX2 = 1f / (prX * prX);
        float invPrY2 = 1f / (prY * prY);

        for (int dy = -prY; dy <= prY; dy++)
        {
            int py = cy + dy;
            if (py < 0 || py >= res) continue;
            float dy2norm = dy * dy * invPrY2;

            for (int dx = -prX; dx <= prX; dx++)
            {
                if (dx * dx * invPrX2 + dy2norm > 1f) continue;
                int px = cx + dx;
                if (px < 0 || px >= res) continue;

                int idx  = py * res + px;
                int mask = _pixelAllowedMask[idx];

                // mask == 0: unregioned, any player allowed.
                // mask != 0: only players whose bit is set are allowed.
                if (mask != 0 && (mask & playerBit) == 0) continue;
                if (_painted[idx]) continue;

                _painted[idx]      = true;
                _outputPixels[idx] = _colorPixels[idx];
                _paintedCount++;
                _dirty = true;
            }
        }
    }

    // Paints a stroke from UV position A to B, sampling densely enough to avoid gaps at any speed.
    public void PaintStroke(Vector2 uvFrom, Vector2 uvTo, int playerSlot, float radiusX, float radiusY)
    {
        float dist  = Vector2.Distance(uvFrom, uvTo);
        float step  = Mathf.Max(radiusX, radiusY) * 0.6f;
        int   steps = Mathf.Max(1, Mathf.CeilToInt(dist / Mathf.Max(step, 0.0001f)));

        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : i / (float)steps;
            Paint(Vector2.Lerp(uvFrom, uvTo, t), playerSlot, radiusX, radiusY);
        }
    }

    private void LateUpdate()
    {
        if (!_dirty || _outputTexture == null) return;
        _dirty = false;
        _outputTexture.SetPixels32(_outputPixels);
        _outputTexture.Apply();
        // SpriteRenderer needs a new Sprite object after every texture upload.
        if (_spriteRenderer != null) AssignOutputTexture();
    }

    // Instantly fill all remaining pixels with the completed colour image.
    public void ShowCompleted()
    {
        if (_completed) return;
        _completed = true;
        System.Array.Copy(_colorPixels, _outputPixels, _colorPixels.Length);
        _outputTexture.SetPixels32(_outputPixels);
        _outputTexture.Apply();
        AssignOutputTexture();
    }

    // Returns a bitmask of player slots allowed to paint the given UV point.
    // 0 means the pixel belongs to no region — all players are allowed to paint it.
    // If multiple regions overlap, all their player slots are OR-ed into the mask.
    private int BuildAllowedMask(Vector2 uv)
    {
        if (_regions == null) return 0;
        int mask = 0;
        for (int i = 0; i < _regions.Count; i++)
        {
            if (IsPointInPolygon(uv, _regions[i].uvVertices))
                mask |= (1 << _regions[i].playerSlot);
        }
        return mask;
    }

    // Standard ray-casting point-in-polygon test.
    private static bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3) return false;
        bool inside = false;
        int  n      = polygon.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if ((polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y)
                          / (polygon[j].y - polygon[i].y) + polygon[i].x)
                inside = !inside;
        }
        return inside;
    }

    // Blits src into a new resolution x resolution pixel array, bypassing read-only import restrictions.
    private static Color32[] SampleTextureToArray(Texture2D src, int resolution)
    {
        if (src == null)
        {
            var blank = new Color32[resolution * resolution];
            for (int i = 0; i < blank.Length; i++) blank[i] = new Color32(128, 128, 128, 255);
            return blank;
        }

        var rt   = RenderTexture.GetTemporary(resolution, resolution, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tmp  = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
        tmp.ReadPixels(new Rect(0, 0, resolution, resolution), 0, 0);
        tmp.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        var pixels = tmp.GetPixels32();
        Destroy(tmp);
        return pixels;
    }
}
