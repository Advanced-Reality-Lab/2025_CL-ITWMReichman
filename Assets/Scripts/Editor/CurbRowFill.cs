using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class CurbRowFill
{
    const float Frontage = 12f;
    const float FaceGap = 2.2f;
    const float GroundY = -5.18f;

    public static string Run(float minX, float maxX, float minZ, float maxZ)
    {
        var parent = GameObject.Find("IsraeliBuildings");
        var curbs = GameObject.Find("IsraeliCurbs");
        if (parent == null || curbs == null) return "missing city";

        int removed = ClearNamed("Row1_");
        removed += ClearNamed("CurbRow_");

        var points = new List<Vector3>(9000);
        var chains = new List<List<Vector3>>();
        for (int c = 0; c < curbs.transform.childCount; c++)
        {
            if (c == 4) continue;
            var mf = curbs.transform.GetChild(c).GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var verts = mf.sharedMesh.vertices;
            var tr = mf.transform;
            int segCount = verts.Length / 24;
            List<Vector3> chain = null;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int s = 0; s < segCount; s++)
            {
                Vector3 acc = Vector3.zero;
                int baseV = s * 24;
                for (int v = 0; v < 24; v++) acc += tr.TransformPoint(verts[baseV + v]);
                acc /= 24f;
                acc.y = 0f;
                if (!hasPrev || Vector3.Distance(acc, prev) > 2.4f)
                {
                    if (chain != null && chain.Count >= 8) chains.Add(chain);
                    chain = new List<Vector3>(64);
                }
                chain.Add(acc);
                points.Add(acc);
                prev = acc;
                hasPrev = true;
            }
            if (chain != null && chain.Count >= 8) chains.Add(chain);
        }

        var grid = new Dictionary<long, List<int>>();
        for (int i = 0; i < points.Count; i++)
        {
            long key = Key(points[i].x, points[i].z);
            List<int> list;
            if (!grid.TryGetValue(key, out list))
            {
                list = new List<int>(4);
                grid.Add(key, list);
            }
            list.Add(i);
        }

        var prefabs = new GameObject[]
        {
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_TelAvivBld_1_Meshy.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_ApartmentBuilding.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_BauhausBld_1_Toto.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_BauhausApartment.prefab"),
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_ArabicBuilding.prefab")
        };
        for (int p = 0; p < prefabs.Length; p++) if (prefabs[p] == null) return "missing prefab " + p;

        var occupied = new List<Bounds>();
        var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            string n = r.transform.root.name;
            if (n.Contains("IsraeliCurbs") || n.Contains("Landschaft")) continue;
            var b = r.bounds;
            if (b.size.y < 1.4f) continue;
            if (b.size.x > 40f || b.size.z > 40f) continue;
            b.Expand(0.4f);
            occupied.Add(b);
        }

        var ground = BuildGround();
        int placed = 0;
        int noRoad = 0;
        int blocked = 0;
        for (int ci = 0; ci < chains.Count; ci++)
        {
            var chain = chains[ci];
            float carry = 8f;
            Vector3 prevTan = Vector3.zero;
            for (int s = 1; s < chain.Count - 1; s++)
            {
                Vector3 p0 = chain[s - 1];
                Vector3 p1 = chain[s];
                Vector3 p2 = chain[s + 1];
                Vector3 tan = p2 - p0;
                tan.y = 0f;
                float len = tan.magnitude;
                if (len < 0.01f) continue;
                tan /= len;
                if (prevTan.sqrMagnitude > 0.1f && Vector3.Dot(prevTan, tan) < 0.82f) carry = 9f;
                else carry += Vector3.Distance(p1, p0);
                prevTan = tan;
                if (carry < 11.5f) continue;
                if (p1.x < minX || p1.x > maxX || p1.z < minZ || p1.z > maxZ) { carry = 0f; continue; }
                if (p1.x > 165f && p1.x < 270f && p1.z < -95f && p1.z > -195f) { carry = 0f; continue; }

                Vector3 across;
                if (!StreetOut(ground, p1, tan, out across)) { noRoad++; carry = 8f; continue; }
                if (Spawn(parent.transform, prefabs, placed, p1, across, occupied)) { placed++; carry = 0f; }
                else { blocked++; carry = 10.4f; }
            }
        }

        return "points " + points.Count + " chains " + chains.Count + " removed " + removed + " placed " + placed + " noRoad " + noRoad + " blocked " + blocked + " occ " + occupied.Count;
    }

    class Ground
    {
        public Vector2[] a;
        public Vector2[] b;
        public Vector2[] c;
        public byte[] road;
        public Dictionary<long, List<int>> grid;
    }

    static Ground BuildGround()
    {
        var g = new Ground();
        var boden = GameObject.Find("boden_stadt");
        g.grid = new Dictionary<long, List<int>>();
        if (boden == null) return g;
        var mf = boden.GetComponent<MeshFilter>();
        var mesh = mf.sharedMesh;
        var tr = boden.transform;
        var verts = mesh.vertices;
        var aa = new List<Vector2>();
        var bb = new List<Vector2>();
        var cc = new List<Vector2>();
        var road = new List<byte>();
        for (int sub = 0; sub < mesh.subMeshCount && sub <= 5; sub++)
        {
            var tris = mesh.GetTriangles(sub);
            byte isRoad = (byte)(sub <= 4 ? 1 : 0);
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector3 wa = tr.TransformPoint(verts[tris[t]]);
                Vector3 wb = tr.TransformPoint(verts[tris[t + 1]]);
                Vector3 wc = tr.TransformPoint(verts[tris[t + 2]]);
                int index = aa.Count;
                aa.Add(new Vector2(wa.x, wa.z));
                bb.Add(new Vector2(wb.x, wb.z));
                cc.Add(new Vector2(wc.x, wc.z));
                road.Add(isRoad);
                float minx = Mathf.Min(wa.x, Mathf.Min(wb.x, wc.x));
                float maxx = Mathf.Max(wa.x, Mathf.Max(wb.x, wc.x));
                float minz = Mathf.Min(wa.z, Mathf.Min(wb.z, wc.z));
                float maxz = Mathf.Max(wa.z, Mathf.Max(wb.z, wc.z));
                int x0 = Mathf.FloorToInt(minx / 16f);
                int x1 = Mathf.FloorToInt(maxx / 16f);
                int z0 = Mathf.FloorToInt(minz / 16f);
                int z1 = Mathf.FloorToInt(maxz / 16f);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        long key = ((long)ix << 32) | (uint)iz;
                        List<int> list;
                        if (!g.grid.TryGetValue(key, out list))
                        {
                            list = new List<int>();
                            g.grid.Add(key, list);
                        }
                        list.Add(index);
                    }
            }
        }
        g.a = aa.ToArray();
        g.b = bb.ToArray();
        g.c = cc.ToArray();
        g.road = road.ToArray();
        return g;
    }

    static int GroundKind(Ground g, Vector3 p)
    {
        if (g.a == null) return 0;
        int ix = Mathf.FloorToInt(p.x / 16f);
        int iz = Mathf.FloorToInt(p.z / 16f);
        long key = ((long)ix << 32) | (uint)iz;
        List<int> list;
        if (!g.grid.TryGetValue(key, out list)) return 0;
        int kind = 0;
        for (int i = 0; i < list.Count; i++)
        {
            int t = list[i];
            Vector2 a = g.a[t];
            Vector2 b = g.b[t];
            Vector2 c = g.c[t];
            if (p.x < a.x && p.x < b.x && p.x < c.x) continue;
            if (p.x > a.x && p.x > b.x && p.x > c.x) continue;
            if (p.z < a.y && p.z < b.y && p.z < c.y) continue;
            if (p.z > a.y && p.z > b.y && p.z > c.y) continue;
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.z - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.z - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.z - a.y);
            if ((d1 < 0f || d2 < 0f || d3 < 0f) && (d1 > 0f || d2 > 0f || d3 > 0f)) continue;
            if (g.road[t] == 1) return 2;
            kind = 1;
        }
        return kind;
    }

    static bool StreetOut(Ground g, Vector3 p, Vector3 tan, out Vector3 across)
    {
        Vector3 side = new Vector3(-tan.z, 0f, tan.x);
        int holePlus = 0;
        int holeMinus = 0;
        float[] far = { 10f, 14f };
        for (int i = 0; i < far.Length; i++)
        {
            if (GroundKind(g, p + side * far[i]) == 0) holePlus++;
            if (GroundKind(g, p - side * far[i]) == 0) holeMinus++;
        }
        if (holePlus > 0 && holeMinus == 0) { across = -side; return true; }
        if (holeMinus > 0 && holePlus == 0) { across = side; return true; }

        int roadPlus = 0;
        int roadMinus = 0;
        float[] near = { 3.5f, 5.5f };
        for (int i = 0; i < near.Length; i++)
        {
            if (GroundKind(g, p + side * near[i]) == 2) roadPlus++;
            if (GroundKind(g, p - side * near[i]) == 2) roadMinus++;
        }
        if (roadPlus > roadMinus) { across = side; return true; }
        if (roadMinus > roadPlus) { across = -side; return true; }
        across = side;
        return false;
    }

    static bool Spawn(Transform parent, GameObject[] prefabs, int index, Vector3 curb, Vector3 outward, List<Bounds> occupied)
    {
        GameObject prefab = prefabs[index % prefabs.Length];
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = "CurbRow_" + prefab.name;
        inst.transform.SetParent(parent, true);
        inst.transform.rotation = Quaternion.identity;
        Vector3 native = prefab.transform.localScale;
        inst.transform.localScale = native;
        inst.transform.position = Vector3.zero;
        float[] fronts = { 16f, 12f, 14f, 11f, 15f };
        float frontage = fronts[index % fronts.Length];
        float longSide = Mathf.Max(Span(inst, Vector3.right), Span(inst, Vector3.forward));
        float xz = frontage / Mathf.Max(0.4f, longSide);
        float hy = (index % 3 == 1) ? 1.4f : 1.08f;
        inst.transform.localScale = new Vector3(native.x * xz, native.y * xz * hy, native.z * xz);
        var facing = inst.GetComponent<BuildingFacing>();
        float yoff = facing != null ? facing.yawOffset : 0f;
        float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + yoff;
        inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        float depth = Mathf.Max(3f, Span(inst, outward));
        Vector3 spot = curb - outward * (FaceGap + depth * 0.5f);
        inst.transform.position = new Vector3(spot.x, 0f, spot.z);
        for (int pass = 0; pass < 2; pass++)
        {
            Bounds wb = WorldBounds(inst);
            inst.transform.position += new Vector3(spot.x - wb.center.x, GroundY - wb.min.y, spot.z - wb.center.z);
        }
        Bounds foot = WorldBounds(inst);
        Vector2 center = new Vector2(foot.center.x, foot.center.z);
        for (int i = 0; i < occupied.Count; i++)
        {
            Vector3 oc = occupied[i].center;
            float dx = oc.x - center.x;
            float dz = oc.z - center.y;
            float reach = Mathf.Max(occupied[i].size.x, occupied[i].size.z) * 0.42f + 5.5f;
            if (dx * dx + dz * dz < reach * reach)
            {
                Object.DestroyImmediate(inst);
                return false;
            }
        }
        occupied.Add(foot);
        Undo.RegisterCreatedObjectUndo(inst, "Curb row");
        return true;
    }

    static float Span(GameObject building, Vector3 axis)
    {
        axis.y = 0f;
        axis.Normalize();
        float min = float.MaxValue;
        float max = float.MinValue;
        var rs = building.GetComponentsInChildren<Renderer>();
        for (int ri = 0; ri < rs.Length; ri++)
        {
            Bounds lb = rs[ri].localBounds;
            Vector3 c = lb.center;
            Vector3 e = lb.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 wp = rs[ri].transform.TransformPoint(c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z));
                float d = wp.x * axis.x + wp.z * axis.z;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
        if (min > max) return 0f;
        return max - min;
    }

    static Bounds WorldBounds(GameObject building)
    {
        var rs = building.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static int ClearNamed(string prefix)
    {
        var kill = new List<GameObject>();
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var tr in all)
        {
            if (tr == null) continue;
            if (!tr.name.StartsWith(prefix)) continue;
            if (tr.parent != null && tr.parent.name.StartsWith(prefix)) continue;
            if (tr.position.x > 165f && tr.position.x < 270f && tr.position.z < -95f && tr.position.z > -195f) continue;
            kill.Add(tr.gameObject);
        }
        for (int i = 0; i < kill.Count; i++) Undo.DestroyObjectImmediate(kill[i]);
        return kill.Count;
    }

    static long Key(float x, float z)
    {
        int ix = Mathf.FloorToInt(x / 4f);
        int iz = Mathf.FloorToInt(z / 4f);
        return ((long)ix << 32) | (uint)iz;
    }
}
