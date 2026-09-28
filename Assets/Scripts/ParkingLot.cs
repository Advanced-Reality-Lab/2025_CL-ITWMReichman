using UnityEngine;

// Open-air parking lot: grid of parked cars, entrance driveway from road, P sign at entrance.
[ExecuteAlways]
public class ParkingLot : MonoBehaviour
{
    public int side = 1;           // which side of the street (+1 right, -1 left)
    public float roadEdgeZ = 3.6f; // kerb Z
    public int rows = 3;           // rows of cars deep (away from road)
    public int cols = 5;           // cars per row
    public GameObject[] carPrefabs;
    public Material roadSurfaceMaterial;

    // Build() called manually after properties set
    public void Build()
    {
        float carL = 4.8f;   // space per car along street (X)
        float carW = 2.8f;   // space per car deep into lot (Z)
        float aisleD = 5.0f; // aisle depth between road edge and first car row
        float driveW = 7.0f; // entrance driveway width

        float lotW = cols * carL;
        float lotD = aisleD + rows * carW + 1.0f; // total depth

        var asphaltMat = roadSurfaceMaterial != null ? roadSurfaceMaterial : Mat(new Color(0.22f, 0.22f, 0.22f));
        var lineMat    = Mat(new Color(0.85f, 0.85f, 0.85f));
        var curbMat    = Mat(new Color(0.55f, 0.55f, 0.55f));
        var sidewalkMat = Mat(new Color(0.72f, 0.70f, 0.65f));

        // Sidewalk strip covering the lot frontage (same colour as road tile pavement)
        Block(sidewalkMat,
            new Vector3(lotW * 0.5f, 0.13f, side * (roadEdgeZ + 1.8f)),
            new Vector3(lotW, 0.05f, 3.6f));

        // Entrance cut — asphalt strip from road edge through sidewalk into lot
        Block(asphaltMat,
            new Vector3(lotW * 0.5f, 0.10f, side * (roadEdgeZ + 1.5f)),
            new Vector3(driveW, 0.10f, 3.0f));

        // Main lot asphalt slab (starts after sidewalk)
        float lotStartZ = roadEdgeZ + 3.5f;
        Block(asphaltMat,
            new Vector3(lotW * 0.5f, 0.08f, side * (lotStartZ + (lotD - 3.5f) * 0.5f)),
            new Vector3(lotW, 0.16f, lotD - 3.5f));

        // Low kerb walls: left, right, far sides
        float kH = 0.20f, kT = 0.18f;
        Block(curbMat, new Vector3(0f,     kH * 0.5f, side * (lotStartZ + (lotD - 3.5f) * 0.5f)), new Vector3(kT, kH, lotD - 3.5f));
        Block(curbMat, new Vector3(lotW,   kH * 0.5f, side * (lotStartZ + (lotD - 3.5f) * 0.5f)), new Vector3(kT, kH, lotD - 3.5f));
        Block(curbMat, new Vector3(lotW * 0.5f, kH * 0.5f, side * (roadEdgeZ + lotD)), new Vector3(lotW, kH, kT));

        // Parking bay divider lines
        float carRowStart = lotStartZ + 0.5f;
        for (int c = 0; c <= cols; c++)
            Block(lineMat,
                new Vector3(c * carL, 0.16f, side * (carRowStart + rows * carW * 0.5f)),
                new Vector3(0.07f, 0.02f, rows * carW));

        // Cars — grid, nose-in toward the far wall
        Color[] bodyColors = {
            new Color(0.91f, 0.91f, 0.91f),
            new Color(0.52f, 0.54f, 0.56f),
            new Color(0.12f, 0.18f, 0.38f),
            new Color(0.28f, 0.14f, 0.14f),
            new Color(0.15f, 0.15f, 0.15f),
        };

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                float cx = c * carL + carL * 0.5f;
                float cz = side * (carRowStart + r * carW + carW * 0.5f);
                int ci = (r * cols + c) % bodyColors.Length;
                if (carPrefabs != null && carPrefabs.Length > 0)
                    SpawnPrefabCar(new Vector3(cx, 0f, cz), r * cols + c, bodyColors[ci]);
            }
        }
    }

    void SpawnPrefabCar(Vector3 pos, int idx, Color bodyColor)
    {
        var prefab = carPrefabs[idx % carPrefabs.Length];
        var go = Application.isPlaying
            ? Instantiate(prefab, transform)
#if UNITY_EDITOR
            : (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, transform);
#else
            : Instantiate(prefab, transform);
#endif
        if (go == null) return;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        go.transform.localScale = Vector3.one * 0.82f;
        go.hideFlags = HideFlags.DontSave;

        var rends = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in rends)
        {
            if (r.gameObject.name.ToLower().Contains("tire")) continue;
            var mat = new Material(r.sharedMaterial);
            mat.color = bodyColor;
            r.material = mat;
            break;
        }

        AddPlate(go.transform, new Vector3(0f, 0.3f, 2.1f));
        AddPlate(go.transform, new Vector3(0f, 0.3f, -2.1f));
    }

    void SpawnProceduralCar(Vector3 pos, Color bodyCol)
    {
        var car = new GameObject("Car");
        car.transform.SetParent(transform, false);
        car.transform.localPosition = pos;
        car.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

        var bodyMat = Mat(bodyCol, 0f, 0.85f);
        var darkMat = Mat(bodyCol * 0.6f, 0f, 0.3f);
        var glassMat = Mat(new Color(0.35f, 0.50f, 0.72f), 0.1f, 0.9f);
        var tireMat = Mat(new Color(0.1f, 0.1f, 0.1f));
        var plateMat = Mat(new Color(0.97f, 0.87f, 0.04f));

        CB(car, bodyMat,  new Vector3(0f, 0.22f, 0f),  new Vector3(4.0f, 0.44f, 1.72f));
        CB(car, bodyMat,  new Vector3(0f, 0.66f, 0f),  new Vector3(2.4f, 0.55f, 1.62f));
        CB(car, glassMat, new Vector3(0.95f, 0.72f, 0f), new Vector3(0.06f, 0.44f, 1.40f));
        CB(car, glassMat, new Vector3(-0.92f, 0.68f, 0f), new Vector3(0.06f, 0.38f, 1.38f));
        CB(car, darkMat,  new Vector3(2.08f, 0.18f, 0f), new Vector3(0.16f, 0.28f, 1.72f));
        CB(car, darkMat,  new Vector3(-2.08f, 0.18f, 0f), new Vector3(0.16f, 0.28f, 1.72f));
        CB(car, plateMat, new Vector3(2.06f, 0.28f, 0f), new Vector3(0.04f, 0.12f, 0.44f));
        CB(car, plateMat, new Vector3(-2.06f, 0.28f, 0f), new Vector3(0.04f, 0.12f, 0.44f));

        float[] wx = { 1.3f, 1.3f, -1.3f, -1.3f };
        float[] wz = { 0.86f, -0.86f, 0.86f, -0.86f };
        for (int i = 0; i < 4; i++)
        {
            var tyre = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tyre.transform.SetParent(car.transform, false);
            tyre.transform.localPosition = new Vector3(wx[i], 0.27f, wz[i]);
            tyre.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            tyre.transform.localScale = new Vector3(0.52f, 0.10f, 0.52f);
            tyre.GetComponent<Renderer>().material = tireMat;
            var tc = tyre.GetComponent<Collider>(); if (tc) { if (Application.isPlaying) Destroy(tc); else DestroyImmediate(tc); }
        }
    }

    void SpawnEntranceSign(Vector3 pos)
    {
        var root = new GameObject("LotEntranceSign");
        root.transform.SetParent(transform, false);
        root.transform.localPosition = pos;

        var poleMat   = Mat(new Color(0.62f, 0.62f, 0.62f));
        var blueMat   = Mat(new Color(0.08f, 0.22f, 0.80f));
        var yellowMat = Mat(new Color(0.97f, 0.85f, 0.06f));
        var whiteMat  = Mat(Color.white);
        var darkMat   = Mat(new Color(0.15f, 0.15f, 0.15f));

        // Pole
        SB(root, poleMat, new Vector3(0f, 2.5f, 0f), new Vector3(0.09f, 5.0f, 0.09f));
        SB(root, poleMat, new Vector3(0f, 0.12f, 0f), new Vector3(0.22f, 0.24f, 0.22f));

        // Bracket arms
        SB(root, poleMat, new Vector3(0.18f, 4.20f, 0f), new Vector3(0.36f, 0.06f, 0.06f));
        SB(root, poleMat, new Vector3(0.18f, 3.42f, 0f), new Vector3(0.36f, 0.06f, 0.06f));
        SB(root, poleMat, new Vector3(0.18f, 2.55f, 0f), new Vector3(0.36f, 0.06f, 0.06f));

        // Yellow schedule board
        SB(root, yellowMat, new Vector3(0.39f, 4.20f, 0f), new Vector3(0.06f, 0.72f, 0.58f));
        SB(root, darkMat,   new Vector3(0.43f, 4.42f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, darkMat,   new Vector3(0.43f, 4.22f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, darkMat,   new Vector3(0.43f, 4.02f, 0f), new Vector3(0.02f, 0.06f, 0.44f));
        SB(root, yellowMat, new Vector3(0.39f, 4.62f, 0f), new Vector3(0.06f, 0.14f, 0.58f));

        // Blue P square
        SB(root, blueMat,  new Vector3(0.39f, 3.42f, 0f), new Vector3(0.06f, 0.58f, 0.58f));
        SB(root, whiteMat, new Vector3(0.44f, 3.42f, -0.10f), new Vector3(0.02f, 0.38f, 0.08f));
        SB(root, whiteMat, new Vector3(0.44f, 3.58f,  0.00f), new Vector3(0.02f, 0.10f, 0.22f));
        SB(root, whiteMat, new Vector3(0.44f, 3.42f,  0.10f), new Vector3(0.02f, 0.22f, 0.08f));

        // Blue round sign
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.transform.SetParent(root.transform, false);
        disc.transform.localPosition = new Vector3(0.39f, 2.55f, 0f);
        disc.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        disc.transform.localScale    = new Vector3(0.38f, 0.03f, 0.38f);
        disc.GetComponent<Renderer>().material = blueMat;
        var dc = disc.GetComponent<Collider>(); if (dc) { if (Application.isPlaying) Destroy(dc); else DestroyImmediate(dc); }
        SB(root, whiteMat, new Vector3(0.43f, 2.55f, 0f), new Vector3(0.02f, 0.30f, 0.03f));
    }

    void AddPlate(Transform parent, Vector3 localPos)
    {
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plate.transform.SetParent(parent, false);
        plate.transform.localPosition = localPos;
        plate.transform.localScale = new Vector3(0.44f, 0.13f, 0.03f);
        plate.GetComponent<Renderer>().material = Mat(new Color(0.97f, 0.87f, 0.04f));
        var col = plate.GetComponent<Collider>();
        if (col) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
    }

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
