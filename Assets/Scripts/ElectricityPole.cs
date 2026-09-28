using UnityEngine;

[ExecuteAlways]
public class ElectricityPole : MonoBehaviour
{
    public float poleSpacing = 50f; // set by ProceduralStreet to match actual pole interval

    void OnEnable() { if (transform.childCount == 0) Build(); }

    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        var grey  = SharedMat(ref _grey,  new Color(0.62f, 0.62f, 0.60f));
        var dark  = SharedMat(ref _dark,  new Color(0.12f, 0.12f, 0.12f), unlit: true);
        var brown = SharedMat(ref _brown, new Color(0.35f, 0.28f, 0.20f));
        var green = SharedMat(ref _green, new Color(0.22f, 0.40f, 0.18f));

        const float h = 7f; // pole height (was 10)
        float s = h / 10f;

        // Main pole
        Cube(grey, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.22f, h, 0.22f));

        // Top cross-arm
        Cube(grey, new Vector3(0f, 9.4f * s, 0f), new Vector3(2.2f, 0.12f, 0.12f));

        // Diagonal braces
        var diagL = Cube(grey, new Vector3(-0.7f, 9.1f * s, 0f), new Vector3(0.08f, 0.7f * s, 0.08f));
        diagL.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
        var diagR = Cube(grey, new Vector3(0.7f, 9.1f * s, 0f), new Vector3(0.08f, 0.7f * s, 0.08f));
        diagR.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);

        // Lower cross-arm
        Cube(grey, new Vector3(0f, 8.0f * s, 0f), new Vector3(1.4f, 0.10f, 0.10f));

        // Insulators — top arm
        for (int i = -1; i <= 1; i++)
        {
            Cylinder(dark, new Vector3(i * 0.8f, 9.55f * s, 0f), new Vector3(0.09f, 0.12f, 0.09f));
            Cylinder(dark, new Vector3(i * 0.8f, 9.3f * s, 0f),  new Vector3(0.06f, 0.08f, 0.06f));
        }
        // Insulators — lower arm
        for (int i = -1; i <= 1; i++)
            Cylinder(dark, new Vector3(i * 0.5f, 8.12f * s, 0f), new Vector3(0.07f, 0.10f, 0.07f));

        // Transformer box
        Cube(brown, new Vector3(0.3f, 5.8f * s, 0f),  new Vector3(0.5f, 0.7f, 0.4f));
        Cube(dark,  new Vector3(0.3f, 5.45f * s, 0f), new Vector3(0.45f, 0.06f, 0.36f));

        // Green base paint
        Cube(green, new Vector3(0f, 0.5f, 0f), new Vector3(0.24f, 1.0f, 0.24f));

        // Wires hang between this pole and the next (same insulator height, sag in the middle).
        float span = Mathf.Max(2f, poleSpacing);
        float sag  = Mathf.Clamp(span * 0.025f, 0.7f, 1.6f);
        float topY = 9.55f * s;
        float lowY = 8.12f * s;

        for (int wi = -1; wi <= 1; wi++)
            SpawnWire(dark, new Vector3(0f, topY, wi * 0.8f), new Vector3(span, topY, wi * 0.8f), sag);
        for (int wi = -1; wi <= 1; wi += 2)
            SpawnWire(dark, new Vector3(0f, lowY, wi * 0.5f), new Vector3(span, lowY, wi * 0.5f), sag * 0.75f);
    }

    void SpawnWire(Material mat, Vector3 from, Vector3 to, float sag)
    {
        const int segs = 8;
        var go = new GameObject("Wire");
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.positionCount = segs + 1;
        lr.startWidth = 0.04f;
        lr.endWidth = 0.04f;
        lr.useWorldSpace = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.numCapVertices = 2;
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            Vector3 p = Vector3.Lerp(from, to, t);
            p.y -= sag * 4f * t * (1f - t);
            lr.SetPosition(i, p);
        }
    }

    GameObject Cube(Material mat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Setup(go, mat, pos, size);
        return go;
    }

    GameObject Cylinder(Material mat, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Setup(go, mat, pos, size);
        return go;
    }

    void Setup(GameObject go, Material mat, Vector3 pos, Vector3 size)
    {
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = mat;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.hideFlags = HideFlags.DontSave;
        var col = go.GetComponent<Collider>();
        if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
    }

    static Material _grey, _dark, _brown, _green;

    static Material SharedMat(ref Material slot, Color c, bool unlit = false)
    {
        if (slot == null)
        {
            var sh = unlit ? Shader.Find("Unlit/Color") : Shader.Find("Standard");
            if (sh == null) sh = Shader.Find("Standard");
            slot = new Material(sh);
            slot.color = c;
            slot.hideFlags = HideFlags.HideAndDontSave;
        }
        return slot;
    }
}
