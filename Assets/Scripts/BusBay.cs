using UnityEngine;

// Bus bay: widens sidewalk on one side with pavement material, red/yellow kerb behind it.
// Parent must be at Y=0. ProceduralStreet sets properties then calls Build() manually.
[ExecuteAlways]
public class BusBay : MonoBehaviour
{
    public int side = 1;
    public float bayLength = 12f;
    public float extraWidth = 3.0f;
    public float roadEdgeZ = 3.6f;
    public float roadSurfaceY = 0.15f;
    public Material roadSurfaceMaterial; // should be the pavement/sidewalk material

    public void Build()
    {
        float slabTop = roadSurfaceY + 0.003f;
        float slabH   = slabTop;
        float slabCentreZ = roadEdgeZ + extraWidth * 0.5f;

        var pavMat = roadSurfaceMaterial != null ? roadSurfaceMaterial : Mat(new Color(0.72f, 0.70f, 0.65f));

        Block(pavMat,
            new Vector3(bayLength * 0.5f, slabTop - slabH * 0.5f, side * slabCentreZ),
            new Vector3(bayLength, slabH, extraWidth));
    }

    void Block(Material mat, Vector3 lp, Vector3 sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = lp;
        go.transform.localScale = sz;
        go.GetComponent<Renderer>().material = mat;
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
