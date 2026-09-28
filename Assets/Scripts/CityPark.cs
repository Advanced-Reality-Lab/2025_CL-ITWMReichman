using UnityEngine;

// Procedural city park: grass patch with paths, benches, trees.
// Placed just beyond the sidewalk edge. ProceduralStreet calls Build() after setting properties.
[ExecuteAlways]
public class CityPark : MonoBehaviour
{
    public int side = 1;
    public float width = 30f;        // along the street (X)
    public float depth = 12f;        // away from road (Z)
    public float sidewalkEdgeZ = 5.5f;
    public GameObject[] treePrefabs;
    public GameObject[] benchPrefabs;

    public void Build()
    {
        float grassY  = 0.04f;
        float pathY   = 0.055f;
        float parkCentreZ = sidewalkEdgeZ + depth * 0.5f;

        var grassMat = Mat(new Color(0.28f, 0.55f, 0.18f));
        Block(grassMat,
            new Vector3(width * 0.5f, grassY * 0.5f, side * parkCentreZ),
            new Vector3(width, grassY, depth));

        var pathMat = Mat(new Color(0.72f, 0.68f, 0.60f));
        Block(pathMat,
            new Vector3(width * 0.5f, pathY * 0.5f, side * parkCentreZ),
            new Vector3(width, pathY, 1.4f));
        Block(pathMat,
            new Vector3(width * 0.5f, pathY * 0.5f, side * parkCentreZ),
            new Vector3(1.4f, pathY, depth));

        var fenceMat = Mat(new Color(0.55f, 0.52f, 0.48f));
        float fH = 0.55f, fT = 0.08f;
        Block(fenceMat, new Vector3(width * 0.5f, fH * 0.5f, side * sidewalkEdgeZ), new Vector3(width, fH, fT));
        Block(fenceMat, new Vector3(width * 0.5f, fH * 0.5f, side * (sidewalkEdgeZ + depth)), new Vector3(width, fH, fT));
        Block(fenceMat, new Vector3(0f, fH * 0.5f, side * parkCentreZ), new Vector3(fT, fH, depth));
        Block(fenceMat, new Vector3(width, fH * 0.5f, side * parkCentreZ), new Vector3(fT, fH, depth));

        float[] treeXs = { width * 0.18f, width * 0.5f, width * 0.82f };
        float[] treeZs = { sidewalkEdgeZ + depth * 0.25f, sidewalkEdgeZ + depth * 0.75f };
        int tIdx = 0;
        foreach (float tz in treeZs)
        {
            foreach (float tx in treeXs)
            {
                // skip centre cross
                if (Mathf.Abs(tx - width * 0.5f) < 1.5f && Mathf.Abs(tz - parkCentreZ) < 2f) { tIdx++; continue; }
                SpawnPrefab(treePrefabs, new Vector3(tx, 0f, side * tz), tIdx * 73f);
                tIdx++;
            }
        }

        // Benches: 4 facing the central path
        float[] bxs = { width * 0.25f, width * 0.75f };
        foreach (float bx in bxs)
        {
            SpawnPrefab(benchPrefabs, new Vector3(bx, 0f, side * (parkCentreZ - 1.2f)), 0f);
            SpawnPrefab(benchPrefabs, new Vector3(bx, 0f, side * (parkCentreZ + 1.2f)), 180f);
        }
    }

    void SpawnPrefab(GameObject[] prefabs, Vector3 worldPos, float yaw)
    {
        if (prefabs == null || prefabs.Length == 0) return;
        int idx = Mathf.Abs(Mathf.RoundToInt(worldPos.x * 7 + worldPos.z * 13)) % prefabs.Length;
        var pf = prefabs[idx];
        if (pf == null) return;
        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(pf, transform);
        else
#endif
            go = Instantiate(pf, transform);
        if (go == null) return;
        go.transform.localPosition = worldPos;
        // Keep the prefab's authored tilt (banyan/ficus is Z-up) and only add yaw.
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * pf.transform.rotation;
        go.hideFlags = HideFlags.DontSave;

        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y - b.min.y + 0.04f, p.z);
        }
    }

    void Block(Material mat, Vector3 worldPos, Vector3 sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = worldPos;
        go.transform.localScale = sz;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.hideFlags = HideFlags.DontSave;
        var c = go.GetComponent<Collider>();
        if (c) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
    }

    Material Mat(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        return m;
    }
}
