using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(AchromaGame2Controller))]
public class AchromaGame2ControllerEditor : Editor
{
    private int _editLevelIndex  = 0;
    private int _editRegionIndex = -1;

    // P1 Red, P2 Blue, P3 Green, P4 Yellow — matches default player identity colours.
    private static readonly Color[] PlayerColors =
    {
        new Color(1f,  0.25f, 0.25f), // P1 Red
        new Color(0.2f, 0.45f, 1f),   // P2 Blue
        new Color(0.2f, 0.9f,  0.2f), // P3 Green
        new Color(1f,  0.9f,  0.1f),  // P4 Yellow
    };

    private AchromaGame2Controller Controller => (AchromaGame2Controller)target;

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI_Event;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI_Event;
    }

    private void OnSceneGUI_Event(SceneView sv)
    {
        if (target == null) return;
        OnSceneGUI();
    }

    // ── Inspector ──────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        // Diagnostic banner — remove once custom editor is confirmed working.
        EditorGUILayout.HelpBox("[ Game 2 Custom Editor Active ]", MessageType.None);
        DrawDefaultInspector();

        var levels = Controller.levels;
        if (levels == null || levels.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Region Polygon Editor", EditorStyles.boldLabel);

        _editLevelIndex = Mathf.Clamp(_editLevelIndex, 0, levels.Count - 1);
        _editLevelIndex = EditorGUILayout.IntSlider("Editing Level", _editLevelIndex, 0, levels.Count - 1);

        var level = levels[_editLevelIndex];
        if (level.regions == null) level.regions = new List<AchromaGame2Region>();

        // Show which coordinate source is active so the user knows what to expect.
        string boundsSource = "Checking...";
        try
        {
            var floorFr = Controller.FloorRenderer;
            Renderer floorR = floorFr != null ? floorFr.GetComponent<Renderer>() : null;
            if (floorR != null && floorR.bounds.size.magnitude > 0.05f)
                boundsSource = "Floor Renderer bounds";
            else
            {
                var anyFloor = Object.FindFirstObjectByType<ColoringFloorRenderer>();
                Renderer anyR = anyFloor != null ? anyFloor.GetComponent<Renderer>() : null;
                if (anyR != null && anyR.bounds.size.magnitude > 0.05f)
                    boundsSource = "Auto-found ColoringFloorRenderer";
                else if (Object.FindFirstObjectByType<TDTableReceiverBase>() != null)
                    boundsSource = "Arena Renderer via TDTableReceiver";
                else
                    boundsSource = "Fallback 10×10 grid";
            }
        }
        catch (System.Exception ex)
        {
            boundsSource = $"Error: {ex.Message}";
        }

        EditorGUILayout.HelpBox(
            $"Select a region below, then drag its white vertex handles in the Scene view.\n" +
            $"UV (0,0) = bottom-left of floor texture, UV (1,1) = top-right.\n" +
            $"Coordinate source: {boundsSource}",
            MessageType.Info);

        EditorGUILayout.Space();

        // Region list
        for (int i = 0; i < level.regions.Count; i++)
        {
            var r = level.regions[i];
            EditorGUILayout.BeginHorizontal();

            bool  isSelected = _editRegionIndex == i;
            Color pc         = PlayerColors[Mathf.Clamp(r.playerSlot, 0, 3)];
            GUI.backgroundColor = isSelected ? Color.cyan : new Color(pc.r, pc.g, pc.b, 0.55f);

            if (GUILayout.Button($"[{i}] {r.label}  Slot {r.playerSlot}  ({r.uvVertices?.Count ?? 0} pts)"))
                _editRegionIndex = isSelected ? -1 : i;

            GUI.backgroundColor = Color.white;

            // Add vertex button
            if (GUILayout.Button("+Pt", GUILayout.Width(36)))
            {
                Undo.RecordObject(target, "Add Vertex");
                if (r.uvVertices == null) r.uvVertices = new List<Vector2>();
                Vector2 newPt = r.uvVertices.Count > 0
                    ? r.uvVertices[r.uvVertices.Count - 1] + new Vector2(0.05f, 0f)
                    : new Vector2(0.5f, 0.5f);
                newPt.x = Mathf.Clamp01(newPt.x);
                newPt.y = Mathf.Clamp01(newPt.y);
                r.uvVertices.Add(newPt);
                EditorUtility.SetDirty(target);
            }

            // Remove last vertex button
            GUI.enabled = r.uvVertices != null && r.uvVertices.Count > 0;
            if (GUILayout.Button("-Pt", GUILayout.Width(36)))
            {
                Undo.RecordObject(target, "Remove Vertex");
                r.uvVertices.RemoveAt(r.uvVertices.Count - 1);
                EditorUtility.SetDirty(target);
            }
            GUI.enabled = true;

            // Delete region button
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                Undo.RecordObject(target, "Delete Region");
                level.regions.RemoveAt(i);
                if (_editRegionIndex >= level.regions.Count) _editRegionIndex = level.regions.Count - 1;
                EditorUtility.SetDirty(target);
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                break;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Add Region for Player", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        string[] playerLabels = { "P1 Red", "P2 Blue", "P3 Green", "P4 Yellow" };
        for (int pi = 0; pi < 4; pi++)
        {
            GUI.backgroundColor = PlayerColors[pi];
            if (GUILayout.Button(playerLabels[pi]))
            {
                Undo.RecordObject(target, $"Add P{pi + 1} Region");
                var newRegion = new AchromaGame2Region
                {
                    label       = $"P{pi + 1} Region {level.regions.Count}",
                    playerSlot  = pi,
                    editorColor = PlayerColors[pi],
                    uvVertices  = new List<Vector2>
                    {
                        new Vector2(0.25f, 0.25f),
                        new Vector2(0.75f, 0.25f),
                        new Vector2(0.75f, 0.75f),
                        new Vector2(0.25f, 0.75f)
                    }
                };
                level.regions.Add(newRegion);
                _editRegionIndex = level.regions.Count - 1;
                EditorUtility.SetDirty(target);
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        SceneView.RepaintAll();
    }

    // ── Scene view ────────────────────────────────────────────────────────

    private void OnSceneGUI()
    {
        var controller = Controller;
        var levels     = controller.levels;
        if (levels == null || levels.Count == 0) return;

        int levelIdx = Mathf.Clamp(_editLevelIndex, 0, levels.Count - 1);
        var level    = levels[levelIdx];
        if (level.regions == null) return;

        // Resolve floor bounds for UV-to-world projection
        Bounds bounds;
        if (!TryGetFloorBounds(controller, out bounds)) return;

        // Always draw handles on top of scene geometry (depth-test disabled for editor overlays).
        Handles.zTest = CompareFunction.Always;

        // Black wire cube marks the floor UV space used for projection.
        Handles.color = new Color(0f, 0f, 0f, 0.7f);
        Handles.DrawWireCube(bounds.center, bounds.size);
        Vector3 labelPos = bounds.center + new Vector3(0f, bounds.size.y * 0.52f, 0f);
        Handles.Label(labelPos,
            $"Floor UV space  {bounds.size.x:F2} x {bounds.size.y:F2} wu\n" +
            $"center {bounds.center:F2}");

        for (int ri = 0; ri < level.regions.Count; ri++)
        {
            var  region    = level.regions[ri];
            bool isEditing = ri == _editRegionIndex;

            if (region.uvVertices == null || region.uvVertices.Count < 2) continue;

            Color c = region.editorColor;

            // Build world-space vertex array
            var verts = region.uvVertices;
            var worldVerts = new Vector3[verts.Count];
            for (int vi = 0; vi < verts.Count; vi++)
                worldVerts[vi] = UVtoWorld(verts[vi], bounds);

            // Draw filled polygon (convex approximation for display only)
            if (verts.Count >= 3)
            {
                Handles.color = new Color(c.r, c.g, c.b, isEditing ? 0.22f : 0.10f);
                Handles.DrawAAConvexPolygon(worldVerts);
            }

            // Draw outline — DrawAAPolyLine with pixel width is much more visible than DrawLine.
            {
                var loop = new Vector3[worldVerts.Length + 1];
                System.Array.Copy(worldVerts, loop, worldVerts.Length);
                loop[worldVerts.Length] = worldVerts[0];
                Handles.color = new Color(c.r, c.g, c.b, 1f);
                Handles.DrawAAPolyLine(isEditing ? 4f : 2f, loop);
            }

            // Draw centroid label
            Vector3 centroid = Vector3.zero;
            foreach (var v in worldVerts) centroid += v;
            centroid /= worldVerts.Length;
            Handles.Label(centroid, $"{region.label}\nSlot {region.playerSlot}",
                          new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = c } });

            // Draw draggable vertex handles for the selected region
            if (!isEditing) continue;

            for (int vi = 0; vi < verts.Count; vi++)
            {
                Vector3 wp = UVtoWorld(verts[vi], bounds);
                Handles.color = Color.white;

                float hSize = HandleUtility.GetHandleSize(wp) * 0.12f;
                EditorGUI.BeginChangeCheck();
                Vector3 newWp = Handles.FreeMoveHandle(wp, hSize, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "Move Vertex");
                    verts[vi] = ClampUV(WorldToUV(newWp, bounds));
                    EditorUtility.SetDirty(target);
                }

                // Vertex index label
                Handles.Label(wp + Vector3.up * hSize * 0.7f, vi.ToString(),
                              new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = Color.white } });
            }
        }
    }

    // ── UV / World helpers ────────────────────────────────────────────────

    // Tries several fallback sources so handles always appear even before scene setup is complete.
    // Skips any source whose bounds are degenerate (Arena has no sprite assigned in edit mode).
    private static bool TryGetFloorBounds(AchromaGame2Controller controller, out Bounds bounds)
    {
        // 1. Assigned floor renderer — skip if bounds are degenerate (no sprite yet)
        var fr = controller.FloorRenderer;
        if (fr != null)
        {
            var r = fr.GetComponent<Renderer>();
            if (r != null && r.bounds.size.magnitude > 0.05f) { bounds = r.bounds; return true; }
        }

        // 2. Any ColoringFloorRenderer in the scene
        var anyFloor = Object.FindFirstObjectByType<ColoringFloorRenderer>();
        if (anyFloor != null)
        {
            var r = anyFloor.GetComponent<Renderer>();
            if (r != null && r.bounds.size.magnitude > 0.05f) { bounds = r.bounds; return true; }
        }

        // 3. TDTableReceiverBase's arena renderer (read via SerializedObject — works in edit mode)
        var receiver = Object.FindFirstObjectByType<TDTableReceiverBase>();
        if (receiver != null)
        {
            var so   = new SerializedObject(receiver);
            var prop = so.FindProperty("_arenaRenderer");
            if (prop != null && prop.objectReferenceValue is Renderer arenaR && arenaR.bounds.size.magnitude > 0.05f)
            {
                bounds = arenaR.bounds;
                return true;
            }
        }

        // 4. Floor Camera orthographic frustum — most reliable source when Arena has no sprite.
        //    Looks for any orthographic camera whose name contains "floor" or "arena".
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (var cam in cameras)
        {
            if (!cam.orthographic) continue;
            string camName = cam.gameObject.name.ToLowerInvariant();
            if (!camName.Contains("floor") && !camName.Contains("arena")) continue;

            float h  = cam.orthographicSize;
            float w  = h * cam.aspect;
            Vector3 cp = cam.transform.position;
            bounds = new Bounds(
                new Vector3(cp.x, cp.y, cp.z),
                new Vector3(w * 2f, h * 2f, 0.1f));
            return true;
        }

        // 5. Fallback: 10×10 grid centred at origin. Handles will appear;
        //    UV coordinates are stored as [0,1]^2 so they remain valid once
        //    the real floor renderer is assigned later.
        bounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 0.1f));
        return true;
    }

    // Projects UV [0,1]^2 onto the floor renderer's world-space bounding box.
    // UV.x maps to the X axis; UV.y maps to whichever of Y/Z has the larger extent
    // (flat XZ floor → maps to Z; upright XY floor → maps to Y).
    private static Vector3 UVtoWorld(Vector2 uv, Bounds b)
    {
        bool flatFloor = b.size.z >= b.size.y;
        if (flatFloor)
        {
            return new Vector3(
                Mathf.Lerp(b.min.x, b.max.x, uv.x),
                b.center.y + 0.02f,
                Mathf.Lerp(b.min.z, b.max.z, uv.y));
        }
        else
        {
            return new Vector3(
                Mathf.Lerp(b.min.x, b.max.x, uv.x),
                Mathf.Lerp(b.min.y, b.max.y, uv.y),
                b.center.z - 0.02f);
        }
    }

    private static Vector2 WorldToUV(Vector3 world, Bounds b)
    {
        bool flatFloor = b.size.z >= b.size.y;
        if (flatFloor)
        {
            return new Vector2(
                Mathf.InverseLerp(b.min.x, b.max.x, world.x),
                Mathf.InverseLerp(b.min.z, b.max.z, world.z));
        }
        else
        {
            return new Vector2(
                Mathf.InverseLerp(b.min.x, b.max.x, world.x),
                Mathf.InverseLerp(b.min.y, b.max.y, world.y));
        }
    }

    private static Vector2 ClampUV(Vector2 uv) =>
        new Vector2(Mathf.Clamp01(uv.x), Mathf.Clamp01(uv.y));
}
