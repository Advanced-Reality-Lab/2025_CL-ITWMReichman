using UnityEngine;

// Israeli parking bay: blue/white curb, 3 parallel-parked cars with yellow plates, P sign.
// Spawns on BOTH sides of the street.
// Assign carPrefab to use a real car model (e.g. from Asset Store) instead of procedural cars.
[ExecuteAlways]
public class ParkingZone : MonoBehaviour
{
    public float zoneLength = 40f;
    public int side = 1;
    public float curbZ = 3.6f;     // must match asphalt kerb
    public float parkingExtra = 2.6f;
    public GameObject[] carPrefabs;
    public Material roadSurfaceMaterial; // set by ProceduralStreet — matches road tile asphalt

    // Build() is called manually by ProceduralStreet after properties are set

    // Build() is called manually by ProceduralStreet after properties are set
    public void Build()
    {
        SpawnSide(side); // one side only — mirror is handled by alternating pzSideDet across chunks
    }

    void SpawnSide(int s)
    {
        float bayZ = curbZ + parkingExtra;

        // Asphalt bay — same material and height as the carriageway, replacing the sidewalk here.
        float roadTop = 0.17f;
        float slabH = 0.05f;
        float slabCentreZ = curbZ + parkingExtra * 0.5f;
        var asphaltMat = roadSurfaceMaterial != null ? roadSurfaceMaterial : Mat(new Color(0.25f, 0.25f, 0.25f));
        Block(asphaltMat,
            new Vector3(zoneLength * 0.5f, roadTop - slabH * 0.5f, s * slabCentreZ),
            new Vector3(zoneLength, slabH, parkingExtra));

        // Blue/white blocks only on the sidewalk edge of the bay.
        PaintBayKerb(s, bayZ);

        int carCount = 3;
        float carSpacing = 7.0f;
        float startX = (zoneLength - carSpacing * (carCount - 1)) * 0.5f;
        float carZ = s * (curbZ + 1.2f);
        for (int c = 0; c < carCount; c++)
        {
            float carX = startX + c * carSpacing;
            if (carPrefabs != null && carPrefabs.Length > 0)
                SpawnPrefabCar(new Vector3(carX, 0f, carZ), s, c);
        }
    }

    void PaintBayKerb(int s, float z)
    {
        var go = new GameObject("BayCurb");
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        go.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        var stripe = go.AddComponent<CurbStripe>();
        stripe.length = zoneLength;
        stripe.side = s;
        stripe.zOffset = z;
        stripe.stripeHeight = 0.16f;
        stripe.stripeWidth = 0.22f;
        stripe.segmentLength = 0.65f;
        stripe.blueWhite = true;
        stripe.Build();
    }

    void SpawnPrefabCar(Vector3 pos, int s, int idx)
    {
        var prefab = PickCurbCar(idx);
        if (prefab == null) return;
        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
        else
#endif
            go = Instantiate(prefab, transform);
        if (go == null) return;
        go.transform.localPosition = pos;
        // Y=90 so car body runs along X (parallel to street)
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localScale = Vector3.one * 0.85f;
        go.hideFlags = HideFlags.DontSave;
        GroundCar(go);
    }

    static readonly string[] CurbCarNames = { "Hatchback Car_15", "Sport Car_39", "Pick Up_11" };

    GameObject PickCurbCar(int idx)
    {
        if (carPrefabs == null || carPrefabs.Length == 0) return null;
        if (idx < CurbCarNames.Length)
        {
            for (int i = 0; i < carPrefabs.Length; i++)
            {
                if (carPrefabs[i] != null && carPrefabs[i].name == CurbCarNames[idx])
                    return carPrefabs[i];
            }
        }
        return carPrefabs[idx % carPrefabs.Length];
    }

    static void GroundCar(GameObject go)
    {
        var rends = go.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        go.transform.position += new Vector3(0f, -b.min.y + 0.17f, 0f);
    }

    void SpawnProceduralCar(Vector3 localPos, int s, Color bodyCol)
    {
        var car = new GameObject("Car");
        car.transform.SetParent(transform, false);
        car.transform.localPosition = localPos;
        car.transform.localRotation = Quaternion.identity; // parallel to street

        // Materials
        var bodyMat  = Mat(bodyCol, 0.0f, 0.85f);
        var darkMat  = Mat(bodyCol * 0.60f, 0.0f, 0.5f);
        var glassMat = Mat(new Color(0.35f, 0.50f, 0.72f), 0.1f, 0.95f);
        var tireMat  = Mat(new Color(0.10f, 0.10f, 0.10f), 0f, 0.05f);
        var rimMat   = Mat(new Color(0.80f, 0.80f, 0.84f), 0.85f, 0.90f);
        var plateMat = Mat(new Color(0.97f, 0.87f, 0.04f), 0f, 0.4f);
        var lightMat = Mat(new Color(0.98f, 0.95f, 0.80f), 0f, 0.95f);
        var brakeMat = Mat(new Color(0.88f, 0.06f, 0.06f), 0f, 0.7f);
        var chromeMat = Mat(new Color(0.82f, 0.82f, 0.85f), 0.9f, 0.95f);

        float L  = 4.0f;  // car length
        float W  = 1.72f; // car width
        float BH = 0.35f; // body height (bottom slab)
        float CH = 0.58f; // cabin height
        float CL = L * 0.58f; // cabin length

        // Lower body slab
        CB(car, bodyMat, new Vector3(0f, BH * 0.5f, 0f), new Vector3(L, BH, W));

        // Sills (dark lower trim)
        CB(car, darkMat, new Vector3(0f, 0.08f, W * 0.5f + 0.02f), new Vector3(L - 0.3f, 0.16f, 0.04f));
        CB(car, darkMat, new Vector3(0f, 0.08f, -W * 0.5f - 0.02f), new Vector3(L - 0.3f, 0.16f, 0.04f));

        // Cabin — 4 segments to create tapered roofline
        // Front slope
        CB(car, bodyMat, new Vector3( L * 0.22f, BH + CH * 0.4f, 0f), new Vector3(L * 0.10f, CH * 0.8f, W - 0.12f));
        // Main roof
        CB(car, bodyMat, new Vector3( 0.0f, BH + CH * 0.5f, 0f), new Vector3(CL * 0.70f, CH, W - 0.10f));
        // Rear slope
        CB(car, bodyMat, new Vector3(-L * 0.22f, BH + CH * 0.35f, 0f), new Vector3(L * 0.10f, CH * 0.7f, W - 0.14f));
        // Boot
        CB(car, bodyMat, new Vector3(-L * 0.38f, BH + CH * 0.12f, 0f), new Vector3(L * 0.14f, CH * 0.25f, W - 0.14f));
        // Hood
        CB(car, bodyMat, new Vector3( L * 0.38f, BH + CH * 0.12f, 0f), new Vector3(L * 0.14f, CH * 0.25f, W - 0.12f));

        // Windshield
        CB(car, glassMat, new Vector3( L * 0.24f, BH + CH * 0.55f, 0f), new Vector3(0.06f, CH * 0.80f, W - 0.24f));
        // Rear window
        CB(car, glassMat, new Vector3(-L * 0.23f, BH + CH * 0.50f, 0f), new Vector3(0.06f, CH * 0.70f, W - 0.26f));
        // Side windows (front/rear)
        CB(car, glassMat, new Vector3( L * 0.10f, BH + CH * 0.58f, W * 0.5f - 0.01f), new Vector3(L * 0.20f, CH * 0.62f, 0.04f));
        CB(car, glassMat, new Vector3(-L * 0.09f, BH + CH * 0.56f, W * 0.5f - 0.01f), new Vector3(L * 0.18f, CH * 0.58f, 0.04f));
        CB(car, glassMat, new Vector3( L * 0.10f, BH + CH * 0.58f, -W * 0.5f + 0.01f), new Vector3(L * 0.20f, CH * 0.62f, 0.04f));
        CB(car, glassMat, new Vector3(-L * 0.09f, BH + CH * 0.56f, -W * 0.5f + 0.01f), new Vector3(L * 0.18f, CH * 0.58f, 0.04f));

        // Door lines (vertical dark seam)
        CB(car, darkMat, new Vector3( 0.04f, BH * 0.8f, W * 0.51f), new Vector3(0.025f, BH * 0.6f, 0.02f));
        CB(car, darkMat, new Vector3( 0.04f, BH * 0.8f, -W * 0.51f), new Vector3(0.025f, BH * 0.6f, 0.02f));

        // Bumpers
        CB(car, darkMat, new Vector3( L * 0.52f, 0.12f, 0f), new Vector3(0.16f, 0.22f, W));
        CB(car, darkMat, new Vector3(-L * 0.52f, 0.12f, 0f), new Vector3(0.16f, 0.22f, W));

        // Grille (front)
        CB(car, darkMat, new Vector3( L * 0.52f, BH * 0.55f, 0f), new Vector3(0.05f, BH * 0.5f, W * 0.55f));

        // Headlights
        CB(car, lightMat, new Vector3( L * 0.505f, BH * 0.72f,  W * 0.36f), new Vector3(0.05f, 0.09f, 0.22f));
        CB(car, lightMat, new Vector3( L * 0.505f, BH * 0.72f, -W * 0.36f), new Vector3(0.05f, 0.09f, 0.22f));
        // Tail lights
        CB(car, brakeMat, new Vector3(-L * 0.505f, BH * 0.72f,  W * 0.36f), new Vector3(0.05f, 0.10f, 0.25f));
        CB(car, brakeMat, new Vector3(-L * 0.505f, BH * 0.72f, -W * 0.36f), new Vector3(0.05f, 0.10f, 0.25f));

        // Side mirrors
        CB(car, darkMat, new Vector3( L * 0.2f, BH + 0.05f,  W * 0.5f + 0.10f), new Vector3(0.18f, 0.08f, 0.10f));
        CB(car, darkMat, new Vector3( L * 0.2f, BH + 0.05f, -W * 0.5f - 0.10f), new Vector3(0.18f, 0.08f, 0.10f));

        // Chrome trim strip
        CB(car, chromeMat, new Vector3(0f, BH + 0.005f, W * 0.51f), new Vector3(L * 0.72f, 0.025f, 0.015f));
        CB(car, chromeMat, new Vector3(0f, BH + 0.005f, -W * 0.51f), new Vector3(L * 0.72f, 0.025f, 0.015f));

        // Licence plates (yellow, Israeli style)
        CB(car, plateMat, new Vector3( L * 0.505f, 0.28f, 0f), new Vector3(0.04f, 0.12f, 0.44f));
        CB(car, plateMat, new Vector3(-L * 0.505f, 0.28f, 0f), new Vector3(0.04f, 0.12f, 0.44f));

        // Wheels — 4 tyres with rims
        float[] wx = {  L * 0.33f, L * 0.33f, -L * 0.33f, -L * 0.33f };
        float[] wz = {  W * 0.50f, -W * 0.50f, W * 0.50f, -W * 0.50f };
        for (int i = 0; i < 4; i++)
        {
            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.transform.SetParent(car.transform, false);
            tyre.transform.localPosition = new Vector3(wx[i], 0.27f, wz[i]);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(0.54f, 0.105f, 0.54f);
            tyre.GetComponent<Renderer>().material = tireMat;
            var tc = tyre.GetComponent<Collider>(); if (tc) { if (Application.isPlaying) Destroy(tc); else DestroyImmediate(tc); }

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.transform.SetParent(car.transform, false);
            rim.transform.localPosition = new Vector3(wx[i], 0.27f, wz[i] + (wz[i] > 0 ? -0.05f : 0.05f));
            rim.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            rim.transform.localScale = new Vector3(0.36f, 0.115f, 0.36f);
            rim.GetComponent<Renderer>().material = rimMat;
            var rc = rim.GetComponent<Collider>(); if (rc) { if (Application.isPlaying) Destroy(rc); else DestroyImmediate(rc); }

            // Wheel arch (dark quarter-circle over wheel)
            CB(car, darkMat, new Vector3(wx[i], 0.50f, wz[i]), new Vector3(0.60f, 0.10f, 0.22f));
        }
    }

    void SpawnParkingSign(Vector3 pos, int s)
    {
        var root = new GameObject("PSign");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = pos;

        var poleMat   = Mat(new Color(0.62f, 0.62f, 0.62f));
        var blueMat   = Mat(new Color(0.08f, 0.22f, 0.80f));
        var yellowMat = Mat(new Color(0.97f, 0.85f, 0.06f));
        var whiteMat  = Mat(Color.white);
        var darkMat   = Mat(new Color(0.15f, 0.15f, 0.15f));

        // Pole — full height, single piece from ground to top of signs
        SB(root, poleMat, new Vector3(0f, 2.5f, 0f), new Vector3(0.09f, 5.0f, 0.09f));
        // Base block
        SB(root, poleMat, new Vector3(0f, 0.12f, 0f), new Vector3(0.22f, 0.24f, 0.22f));

        // Bracket arm connecting pole to sign faces (horizontal stub)
        SB(root, poleMat, new Vector3(0.18f, 4.20f, 0f), new Vector3(0.36f, 0.06f, 0.06f));
        SB(root, poleMat, new Vector3(0.18f, 3.42f, 0f), new Vector3(0.36f, 0.06f, 0.06f));
        SB(root, poleMat, new Vector3(0.18f, 2.55f, 0f), new Vector3(0.36f, 0.06f, 0.06f));

        // ── Yellow schedule board (top) — flush against bracket ──
        SB(root, yellowMat, new Vector3(0.39f, 4.20f, 0f), new Vector3(0.06f, 0.72f, 0.58f));
        SB(root, darkMat,   new Vector3(0.43f, 4.42f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, darkMat,   new Vector3(0.43f, 4.22f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, darkMat,   new Vector3(0.43f, 4.02f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, yellowMat, new Vector3(0.39f, 4.62f, 0f), new Vector3(0.06f, 0.14f, 0.58f));

        // ── Blue P square (middle) ──
        SB(root, blueMat,  new Vector3(0.39f, 3.42f, 0f), new Vector3(0.06f, 0.58f, 0.58f));
        SB(root, whiteMat, new Vector3(0.44f, 3.42f, -0.10f), new Vector3(0.02f, 0.38f, 0.08f));
        SB(root, whiteMat, new Vector3(0.44f, 3.58f,  0.00f), new Vector3(0.02f, 0.10f, 0.22f));
        SB(root, whiteMat, new Vector3(0.44f, 3.42f,  0.10f), new Vector3(0.02f, 0.22f, 0.08f));

        // ── Blue round sign (bottom) ──
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.transform.SetParent(root.transform, false);
        disc.transform.localPosition = new Vector3(0.39f, 2.55f, 0f);
        disc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        disc.transform.localScale    = new Vector3(0.38f, 0.03f, 0.38f);
        disc.GetComponent<Renderer>().material = blueMat;
        var dc = disc.GetComponent<Collider>(); if (dc) { if (Application.isPlaying) Destroy(dc); else DestroyImmediate(dc); }
        SB(root, whiteMat, new Vector3(0.43f, 2.55f, 0f), new Vector3(0.02f, 0.30f, 0.03f));
    }

    // Helpers
    void Block(Material mat, Vector3 lp, Vector3 sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale = sz;
        go.GetComponent<Renderer>().material = mat;
        var c = go.GetComponent<Collider>(); if (c) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
    }

    void CB(GameObject parent, Material mat, Vector3 lp, Vector3 sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale = sz;
        go.GetComponent<Renderer>().material = mat;
        var c = go.GetComponent<Collider>(); if (c) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
    }

    void SB(GameObject root, Material mat, Vector3 lp, Vector3 sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale = sz;
        go.GetComponent<Renderer>().material = mat;
        var c = go.GetComponent<Collider>(); if (c) { if (Application.isPlaying) Destroy(c); else DestroyImmediate(c); }
    }

    Material Mat(Color c, float metallic = 0f, float smoothness = 0.3f)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }
}
