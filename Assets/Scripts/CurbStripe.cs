using System.Collections.Generic;
using UnityEngine;

// Red-and-white painted raised kerb at the road/sidewalk boundary.
public class CurbStripe : MonoBehaviour
{
    public float length = 100f;
    public float stripeWidth = 0.22f;
    public float stripeHeight = 0.16f;
    public float segmentLength = 0.65f;
    public int side = 1;
    public float zOffset = 3.6f;
    public float gapStart  = -1f;
    public float gapEnd    = -1f;
    public float gap2Start = -1f;
    public float gap2End   = -1f;
    public float altStart = -1f;
    public float altEnd = -1f;
    [Tooltip("If true, paint blue/white parking kerb instead of red/white.")]
    public bool blueWhite = false;
    [Tooltip("If true, paint the whole stripe red/yellow.")]
    public bool redYellow = false;

    static Mesh _cube;
    static Material _red;
    static Material _white;

    public void Build()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        var redList = new List<CombineInstance>(128);
        var whiteList = new List<CombineInstance>(128);
        var yellowList = new List<CombineInstance>(64);
        var cube = CubeMesh();
        float y = stripeHeight * 0.5f - 0.02f;
        var scale = new Vector3(segmentLength - 0.02f, stripeHeight, stripeWidth);

        int count = Mathf.CeilToInt(length / segmentLength);
        for (int i = 0; i < count; i++)
        {
            float segX = i * segmentLength + segmentLength * 0.5f;
            if (gapStart  >= 0 && segX >= gapStart  && segX <= gapEnd)  continue;
            if (gap2Start >= 0 && segX >= gap2Start && segX <= gap2End) continue;

            var ci = new CombineInstance
            {
                mesh = cube,
                transform = Matrix4x4.TRS(
                    new Vector3(segX, y, side * zOffset),
                    Quaternion.identity,
                    scale)
            };
            bool inAlt = altStart >= 0f && segX >= altStart && segX <= altEnd;
            if ((i % 2) == 0) redList.Add(ci);
            else if (inAlt || redYellow) yellowList.Add(ci);
            else whiteList.Add(ci);
        }

        Bake("Red", redList, blueWhite ? BlueMat() : RedMat());
        Bake("White", whiteList, WhiteMat());
        Bake("Yellow", yellowList, YellowMat());
    }

    void Bake(string name, List<CombineInstance> list, Material mat)
    {
        if (list.Count == 0) return;
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.hideFlags = HideFlags.DontSave;
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = "Curb_" + name };
        mesh.CombineMeshes(list.ToArray(), true, true);
        mf.sharedMesh = mesh;
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    static Mesh CubeMesh()
    {
        if (_cube != null) return _cube;
        _cube = new Mesh { name = "CurbCube" };
        _cube.vertices = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3( 0.5f, -0.5f, -0.5f),
            new Vector3( 0.5f,  0.5f, -0.5f), new Vector3(-0.5f,  0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f,  0.5f), new Vector3( 0.5f, -0.5f,  0.5f),
            new Vector3( 0.5f,  0.5f,  0.5f), new Vector3(-0.5f,  0.5f,  0.5f)
        };
        _cube.triangles = new int[]
        {
            0,2,1, 0,3,2,
            4,5,6, 4,6,7,
            0,1,5, 0,5,4,
            3,6,2, 3,7,6,
            0,4,7, 0,7,3,
            1,2,6, 1,6,5
        };
        _cube.RecalculateNormals();
        _cube.RecalculateBounds();
        _cube.hideFlags = HideFlags.HideAndDontSave;
        return _cube;
    }

    static Material _blue;

    static Material _yellow;

    static Material YellowMat()
    {
        if (_yellow == null)
            _yellow = Solid(new Color(0.97f, 0.82f, 0.08f));
        return _yellow;
    }

    static Material BlueMat()
    {
        if (_blue == null)
            _blue = Solid(new Color(0.10f, 0.28f, 0.82f));
        return _blue;
    }

    static Material RedMat()
    {
        if (_red == null)
        {
            _red = Solid(new Color(0.92f, 0.06f, 0.06f));
        }
        return _red;
    }

    static Material WhiteMat()
    {
        if (_white == null)
        {
            _white = Solid(new Color(0.95f, 0.95f, 0.95f));
        }
        return _white;
    }

    static Material Solid(Color c)
    {
        var m = new Material(Shader.Find("Standard"));
        m.color = c;
        m.SetFloat("_Glossiness", 0.2f);
        m.hideFlags = HideFlags.HideAndDontSave;
        return m;
    }
}
