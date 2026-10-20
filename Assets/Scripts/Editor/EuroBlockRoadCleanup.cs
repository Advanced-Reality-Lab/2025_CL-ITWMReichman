using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Hides buildings whose footprint sits on the road surface (submeshes 0-4 of boden_stadt).
// Hidden objects are recorded under EuroBlocks/HiddenOnRoad, so Revert All brings them back.
public static partial class EuroBlockLayout
{
    const string OnRoadName = "HiddenOnRoad";
    const float OnRoadFraction = 0.2f;   // share of the footprint on asphalt that counts as "on the road"

    [MenuItem("Tools/Euro Blocks/Remove Buildings On Roads")]
    public static void RemoveOnRoadMenu() { Debug.Log(RemoveBuildingsOnRoads()); }

    public static string RemoveBuildingsOnRoads()
    {
        var root = GameObject.Find("EuroBlocks");
        if (root == null) return "run Apply All first";
        var kind = BuildKindGrid();

        var holderT = root.transform.Find(OnRoadName);
        GameObject holder;
        if (holderT != null) holder = holderT.gameObject;
        else
        {
            holder = new GameObject(OnRoadName);
            Undo.RegisterCreatedObjectUndo(holder, "Remove buildings on roads");
            holder.transform.SetParent(root.transform, false);
            holder.AddComponent<EuroBlockRecord>();
        }
        var rec = holder.GetComponent<EuroBlockRecord>();

        var candidates = new List<GameObject>();
        var old = GameObject.Find("IsraeliBuildings");
        if (old != null) foreach (Transform t in old.transform) candidates.Add(t.gameObject);
        foreach (Transform block in root.transform)
            foreach (Transform t in block)
                if (t.name.StartsWith("Row_") || t.name.StartsWith("Corner_")) candidates.Add(t.gameObject);

        int hidden = 0, checkedCount = 0;
        var names = new System.Text.StringBuilder();
        foreach (var go in candidates)
        {
            if (!go.activeInHierarchy) continue;
            Vector3 p = go.transform.position;
            if (p.x > 165f && p.x < 270f && p.z < -95f && p.z > -195f) continue;   // protected street
            checkedCount++;
            if (RoadShare(go, kind) < OnRoadFraction) continue;
            Undo.RecordObject(go, "Remove buildings on roads");
            go.SetActive(false);
            rec.hidden.Add(go);
            hidden++;
            if (hidden <= 40) names.Append(go.name).Append(" @ ").Append(Mathf.RoundToInt(p.x)).Append(',').Append(Mathf.RoundToInt(p.z)).Append('\n');
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
        return "checked " + checkedCount + " buildings, hid " + hidden + " standing on the road\n" + names;
    }

    // Samples each tall renderer's footprint (its local box, slightly inset) on a ~1 m grid.
    static float RoadShare(GameObject go, byte[] kind)
    {
        int onRoad = 0, total = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (r.bounds.size.y < 2f) continue;   // skip awnings, signs, plants
            Bounds lb = r.localBounds;
            Vector3 ex = r.transform.TransformVector(new Vector3(lb.size.x, 0f, 0f));
            Vector3 ez = r.transform.TransformVector(new Vector3(0f, 0f, lb.size.z));
            int nx = Mathf.Clamp(Mathf.CeilToInt(new Vector2(ex.x, ex.z).magnitude), 2, 60);
            int nz = Mathf.Clamp(Mathf.CeilToInt(new Vector2(ez.x, ez.z).magnitude), 2, 60);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    float u = Mathf.Lerp(0.1f, 0.9f, (i + 0.5f) / nx) - 0.5f;
                    float v = Mathf.Lerp(0.1f, 0.9f, (j + 0.5f) / nz) - 0.5f;
                    Vector3 local = lb.center + new Vector3(u * lb.size.x, 0f, v * lb.size.z);
                    Vector3 w = r.transform.TransformPoint(local);
                    int c = Cell(w.x, w.z);
                    if (c < 0) continue;
                    total++;
                    if (kind[c] == 2) onRoad++;
                }
        }
        return total == 0 ? 0f : (float)onRoad / total;
    }
}
