using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Rebuilds a city block as a European perimeter block: continuous row of buildings on the
// building line (behind the sidewalk), corner buildings, and a green inner courtyard.
public static partial class EuroBlockLayout
{
    const float X0 = -290f, Z0 = -335f;
    const int NX = 690, NZ = 560;
    const float GroundY = -5.18f;
    const float Sidewalk = 3f;
    const float Depth = 12f;
    const float CornerTurnDeg = 35f;
    const string TemplateDir = "Assets/IsraeliStreet/_Templates/";
    static readonly string[] RowPrefabs = { "Bld_TelAvivBld_1_Meshy", "Bld_ApartmentBuilding", "Bld_BauhausApartment" };
    const string CornerPrefab = "Bld_BauhausBld_1_Toto";

    class Slot
    {
        public Vector3 center, tangent, outward;
        public float width, depth;
        public GameObject prefab;
        public bool corner;
    }

    [MenuItem("Tools/Euro Blocks/Revert All")]
    public static void RevertAllMenu() { Debug.Log(RevertAll()); }

    public static string ApplyAt(float x, float z)
    {
        var kind = BuildKindGrid();
        int seed = Cell(x, z);
        if (seed < 0 || kind[seed] != 1) return "seed is not on a block (kind " + (seed < 0 ? -1 : kind[seed]) + ")";
        var mask = Flood(kind, seed);
        return ApplyMask(mask, "Block_" + Mathf.RoundToInt(x) + "_" + Mathf.RoundToInt(z));
    }

    [MenuItem("Tools/Euro Blocks/Apply All")]
    public static void ApplyAllMenu() { Debug.Log(ApplyAll()); }

    // Rebuild every block (connected sidewalk area) between minArea and maxArea m².
    public static string ApplyAll(int minArea = 900, int maxArea = 25000)
    {
        RevertAll();
        var kind = BuildKindGrid();
        var seen = new bool[NX * NZ];
        int done = 0, skipped = 0, totalHidden = 0;
        var log = new System.Text.StringBuilder();
        for (int i = 0; i < kind.Length; i++)
        {
            if (seen[i] || kind[i] != 1) continue;
            var mask = Flood(kind, i);
            int area = 0;
            double sx = 0, sz = 0;
            for (int j = 0; j < mask.Length; j++)
                if (mask[j]) { seen[j] = true; area++; sx += j % NX; sz += j / NX; }
            if (area < minArea || area > maxArea) { skipped++; continue; }
            float cx = X0 + (float)(sx / area), cz = Z0 + (float)(sz / area);
            if (cx > 165f && cx < 270f && cz < -95f && cz > -195f) { skipped++; continue; }
            string res = ApplyMask(mask, "Block_" + Mathf.RoundToInt(cx) + "_" + Mathf.RoundToInt(cz));
            log.Append(res).Append('\n');
            if (res.StartsWith("Block_")) done++; else skipped++;
        }
        // Open ground fully enclosed by roads (no sidewalk mesh) — scattered buildings on bare terrain.
        var holeSeen = new bool[NX * NZ];
        for (int i = 0; i < kind.Length; i++)
        {
            if (holeSeen[i] || kind[i] != 0) continue;
            bool border;
            var mask = FloodKind(kind, i, 0, holeSeen, out border);
            int area = 0;
            double sx = 0, sz = 0;
            for (int j = 0; j < mask.Length; j++)
                if (mask[j]) { area++; sx += j % NX; sz += j / NX; }
            if (border || area < minArea) continue;
            float cx = X0 + (float)(sx / area), cz = Z0 + (float)(sz / area);
            if (cx > 165f && cx < 270f && cz < -95f && cz > -195f) continue;
            string res = ApplyMask(mask, "Open_" + Mathf.RoundToInt(cx) + "_" + Mathf.RoundToInt(cz));
            log.Append(res).Append('\n');
            if (res.StartsWith("Open_")) done++;
        }

        var root = GameObject.Find("EuroBlocks");
        if (root != null)
            foreach (var r in root.GetComponentsInChildren<EuroBlockRecord>()) totalHidden += r.hidden.Count;
        return "blocks " + done + ", skipped " + skipped + ", hidden " + totalHidden + "\n" + log;
    }

    static string ApplyMask(bool[] mask, string blockName)
    {
        Smooth(mask);
        var ring = Trace(mask);
        if (ring.Count < 16) return "block outline too small";

        var poly = SimplifyClosed(ring, 1.2f);
        MergeShortEdges(poly, 6f);
        if (SignedArea(poly) < 0f) poly.Reverse();
        var line = Offset(poly, Sidewalk);

        var rows = new List<GameObject>();
        foreach (var n in RowPrefabs) rows.Add(Load(n));
        var cornerPrefab = Load(CornerPrefab);
        if (rows.Contains(null) || cornerPrefab == null) return "missing template prefab";

        var slots = PlanSlots(line, rows, cornerPrefab);

        var root = EnsureRoot();
        var old = root.transform.Find(blockName);
        if (old != null) RevertBlock(old.gameObject);

        var blockGo = new GameObject(blockName);
        Undo.RegisterCreatedObjectUndo(blockGo, "Euro block");
        blockGo.transform.SetParent(root.transform, true);
        var record = blockGo.AddComponent<EuroBlockRecord>();
        HideExisting(mask, record);

        var placed = new List<GameObject>();
        var natural = new List<float>();
        foreach (var s in slots)
        {
            var go = Fit(s, blockGo.transform);
            placed.Add(go);
            natural.Add(WorldBounds(go).size.y);
        }
        UnifyHeights(placed, natural, slots);
        BuildCourtyard(mask, blockGo.transform);

        return blockName + ": outline " + ring.Count + " px, polygon " + poly.Count + " vertices, slots " + slots.Count +
               " (corners " + slots.FindAll(s => s.corner).Count + "), hidden " + record.hidden.Count;
    }

    public static string RevertAll()
    {
        var root = GameObject.Find("EuroBlocks");
        if (root == null) return "nothing to revert";
        int n = root.transform.childCount;
        for (int i = n - 1; i >= 0; i--) RevertBlock(root.transform.GetChild(i).gameObject);
        Undo.DestroyObjectImmediate(root);
        return "reverted " + n + " blocks";
    }

    static void RevertBlock(GameObject block)
    {
        var rec = block.GetComponent<EuroBlockRecord>();
        if (rec != null)
            foreach (var h in rec.hidden)
                if (h != null) { Undo.RecordObject(h, "Euro block revert"); h.SetActive(true); }
        Undo.DestroyObjectImmediate(block);
    }

    // ---------- grid ----------

    static int Cell(float x, float z)
    {
        int ix = Mathf.FloorToInt(x - X0), iz = Mathf.FloorToInt(z - Z0);
        if (ix < 0 || iz < 0 || ix >= NX || iz >= NZ) return -1;
        return iz * NX + ix;
    }

    static Vector3 CellCenter(int ix, int iz) { return new Vector3(X0 + ix + 0.5f, 0f, Z0 + iz + 0.5f); }

    // 0 = no city ground, 1 = sidewalk/block surface, 2 = road
    static byte[] BuildKindGrid()
    {
        var kind = new byte[NX * NZ];
        var boden = GameObject.Find("boden_stadt");
        var mesh = boden.GetComponent<MeshFilter>().sharedMesh;
        var tr = boden.transform;
        var v = mesh.vertices;
        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            byte k = (byte)(sub <= 4 ? 2 : 1);
            var t = mesh.GetTriangles(sub);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = tr.TransformPoint(v[t[i]]), b = tr.TransformPoint(v[t[i + 1]]), c = tr.TransformPoint(v[t[i + 2]]);
                int ix0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - X0));
                int ix1 = Mathf.Min(NX - 1, Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - X0));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - Z0));
                int iz1 = Mathf.Min(NZ - 1, Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - Z0));
                for (int iz = iz0; iz <= iz1; iz++)
                    for (int ix = ix0; ix <= ix1; ix++)
                    {
                        float px = X0 + ix + 0.5f, pz = Z0 + iz + 0.5f;
                        float d1 = (px - b.x) * (a.z - b.z) - (a.x - b.x) * (pz - b.z);
                        float d2 = (px - c.x) * (b.z - c.z) - (b.x - c.x) * (pz - c.z);
                        float d3 = (px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z);
                        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                        if (neg && pos) continue;
                        int id = iz * NX + ix;
                        if (kind[id] < k) kind[id] = k;
                    }
            }
        }
        return kind;
    }

    static bool[] Flood(byte[] kind, int seed)
    {
        var mask = new bool[NX * NZ];
        var q = new Queue<int>();
        mask[seed] = true;
        q.Enqueue(seed);
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            int px = p % NX;
            if (px > 0) Push(p - 1);
            if (px < NX - 1) Push(p + 1);
            if (p >= NX) Push(p - NX);
            if (p < NX * (NZ - 1)) Push(p + NX);
        }
        return mask;

        void Push(int n)
        {
            if (mask[n] || kind[n] != 1) return;
            mask[n] = true;
            q.Enqueue(n);
        }
    }

    static bool[] FloodKind(byte[] kind, int seed, byte k, bool[] seen, out bool touchesBorder)
    {
        var mask = new bool[NX * NZ];
        var q = new Queue<int>();
        mask[seed] = seen[seed] = true;
        q.Enqueue(seed);
        touchesBorder = false;
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            int px = p % NX, pz = p / NX;
            if (px == 0 || pz == 0 || px == NX - 1 || pz == NZ - 1) touchesBorder = true;
            if (px > 0) Visit(p - 1);
            if (px < NX - 1) Visit(p + 1);
            if (pz > 0) Visit(p - NX);
            if (pz < NZ - 1) Visit(p + NX);
        }
        return mask;

        void Visit(int n)
        {
            if (mask[n] || kind[n] != k) return;
            mask[n] = seen[n] = true;
            q.Enqueue(n);
        }
    }

    static void Smooth(bool[] mask)
    {
        var tmp = (bool[])mask.Clone();
        for (int iz = 1; iz < NZ - 1; iz++)
            for (int ix = 1; ix < NX - 1; ix++)
            {
                int p = iz * NX + ix;
                int n = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (tmp[p + dz * NX + dx]) n++;
                mask[p] = tmp[p] ? n >= 4 : n >= 6;
            }
    }

    // Moore-neighbour boundary trace; returns ordered outline cell centres.
    static List<Vector3> Trace(bool[] mask)
    {
        var pts = new List<Vector3>();
        int start = -1;
        for (int i = 0; i < mask.Length && start < 0; i++) if (mask[i]) start = i;
        if (start < 0) return pts;

        int[] dx = { -1, -1, 0, 1, 1, 1, 0, -1 };
        int[] dz = { 0, 1, 1, 1, 0, -1, -1, -1 };
        int cx = start % NX, cz = start / NX;
        int back = 0;
        int sx = cx, sz = cz;
        int firstDir = -1;
        for (int step = 0; step < 200000; step++)
        {
            pts.Add(CellCenter(cx, cz));
            int found = -1;
            for (int k = 1; k <= 8; k++)
            {
                int d = (back + k) % 8;
                int nx = cx + dx[d], nz = cz + dz[d];
                if (nx < 0 || nz < 0 || nx >= NX || nz >= NZ) continue;
                if (!mask[nz * NX + nx]) continue;
                found = d;
                break;
            }
            if (found < 0) break;
            int prevD = (found + 7) % 8;
            int bx = cx + dx[prevD] - (cx + dx[found]);
            int bz = cz + dz[prevD] - (cz + dz[found]);
            cx += dx[found];
            cz += dz[found];
            back = DirIndex(bx, bz, dx, dz);
            if (step == 0) firstDir = found;
            else if (cx == sx && cz == sz && found == firstDir) break;
            if (cx == sx && cz == sz && step > 0 && firstDir >= 0)
            {
                // stop once we re-enter the start cell; Jacob's criterion is close enough for blocks
                break;
            }
        }
        return pts;
    }

    static int DirIndex(int x, int z, int[] dx, int[] dz)
    {
        for (int i = 0; i < 8; i++) if (dx[i] == x && dz[i] == z) return i;
        return 0;
    }

    // ---------- polygon ----------

    static List<Vector3> SimplifyClosed(List<Vector3> ring, float eps)
    {
        int far = 0;
        float best = -1f;
        for (int i = 0; i < ring.Count; i++)
        {
            float d = (ring[i] - ring[0]).sqrMagnitude;
            if (d > best) { best = d; far = i; }
        }
        var a = ring.GetRange(0, far + 1);
        var b = ring.GetRange(far, ring.Count - far);
        b.Add(ring[0]);
        var ra = DP(a, eps);
        var rb = DP(b, eps);
        ra.RemoveAt(ra.Count - 1);
        rb.RemoveAt(rb.Count - 1);
        ra.AddRange(rb);
        return ra;
    }

    static List<Vector3> DP(List<Vector3> pts, float eps)
    {
        var keep = new bool[pts.Count];
        keep[0] = keep[pts.Count - 1] = true;
        var stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var r = stack.Pop();
            float maxD = 0f;
            int idx = -1;
            for (int i = r.x + 1; i < r.y; i++)
            {
                float d = DistToSeg(pts[i], pts[r.x], pts[r.y]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx >= 0 && maxD > eps)
            {
                keep[idx] = true;
                stack.Push(new Vector2Int(r.x, idx));
                stack.Push(new Vector2Int(idx, r.y));
            }
        }
        var res = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) res.Add(pts[i]);
        return res;
    }

    static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector3.Distance(p, a + ab * t);
    }

    // Collapse short edges (rounded road corners) into a single sharp corner.
    static void MergeShortEdges(List<Vector3> poly, float minLen)
    {
        bool changed = true;
        while (changed && poly.Count > 4)
        {
            changed = false;
            for (int i = 0; i < poly.Count && poly.Count > 4; i++)
            {
                int j = (i + 1) % poly.Count;
                if (Vector3.Distance(poly[i], poly[j]) >= minLen) continue;
                Vector3 p0 = poly[(i - 1 + poly.Count) % poly.Count];
                Vector3 p3 = poly[(j + 1) % poly.Count];
                Vector3 merged;
                if (!LineIntersect(p0, poly[i], poly[j], p3, out merged) ||
                    Vector3.Distance(merged, (poly[i] + poly[j]) * 0.5f) > 8f)
                    merged = (poly[i] + poly[j]) * 0.5f;
                poly[i] = merged;
                poly.RemoveAt(j);
                changed = true;
                break;
            }
        }
    }

    static bool LineIntersect(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, out Vector3 hit)
    {
        Vector2 p = new Vector2(a0.x, a0.z), r = new Vector2(a1.x - a0.x, a1.z - a0.z);
        Vector2 q = new Vector2(b0.x, b0.z), s = new Vector2(b1.x - b0.x, b1.z - b0.z);
        float den = r.x * s.y - r.y * s.x;
        hit = Vector3.zero;
        if (Mathf.Abs(den) < 1e-4f) return false;
        float t = ((q.x - p.x) * s.y - (q.y - p.y) * s.x) / den;
        hit = new Vector3(p.x + r.x * t, 0f, p.y + r.y * t);
        return true;
    }

    static float SignedArea(List<Vector3> poly)
    {
        float a = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            var p = poly[i];
            var q = poly[(i + 1) % poly.Count];
            a += p.x * q.z - q.x * p.z;
        }
        return a * 0.5f;
    }

    static Vector3 LeftNormal(Vector3 d) { return new Vector3(-d.z, 0f, d.x); }

    static List<Vector3> Offset(List<Vector3> poly, float dist)
    {
        var res = new List<Vector3>(poly.Count);
        for (int i = 0; i < poly.Count; i++)
        {
            Vector3 prev = poly[(i - 1 + poly.Count) % poly.Count];
            Vector3 next = poly[(i + 1) % poly.Count];
            Vector3 n1 = LeftNormal((poly[i] - prev).normalized);
            Vector3 n2 = LeftNormal((next - poly[i]).normalized);
            float denom = Mathf.Max(0.35f, 1f + Vector3.Dot(n1, n2));
            res.Add(poly[i] + (n1 + n2) * (dist / denom));
        }
        return res;
    }

    // ---------- layout ----------

    static List<Slot> PlanSlots(List<Vector3> line, List<GameObject> rows, GameObject cornerPrefab)
    {
        int n = line.Count;
        var isCorner = new bool[n];
        var convex = new bool[n];
        var cornerIdx = new List<int>();
        for (int i = 0; i < n; i++)
        {
            Vector3 dIn = (line[i] - line[(i - 1 + n) % n]).normalized;
            Vector3 dOut = (line[(i + 1) % n] - line[i]).normalized;
            float turn = Vector3.Angle(dIn, dOut);
            convex[i] = dIn.x * dOut.z - dIn.z * dOut.x > 0f;
            if (turn >= CornerTurnDeg) { isCorner[i] = true; cornerIdx.Add(i); }
        }

        var slots = new List<Slot>();
        foreach (int c in cornerIdx)
        {
            if (!convex[c]) continue;
            Vector3 dOut = (line[(c + 1) % n] - line[c]).normalized;
            Vector3 inward = LeftNormal(dOut);
            slots.Add(new Slot
            {
                center = line[c] + dOut * (Depth * 0.5f) + inward * (Depth * 0.5f),
                tangent = dOut,
                outward = -inward,
                width = Depth,
                depth = Depth,
                prefab = cornerPrefab,
                corner = true
            });
        }

        var runs = new List<List<Vector3>>();
        var runTrim = new List<Vector2>();
        if (cornerIdx.Count == 0)
        {
            var loop = new List<Vector3>(line) { line[0] };
            runs.Add(loop);
            runTrim.Add(Vector2.zero);
        }
        else
        {
            for (int k = 0; k < cornerIdx.Count; k++)
            {
                int a = cornerIdx[k], b = cornerIdx[(k + 1) % cornerIdx.Count];
                var run = new List<Vector3>();
                int i = a;
                run.Add(line[i]);
                do { i = (i + 1) % n; run.Add(line[i]); } while (i != b);
                runs.Add(run);
                float trimStart = convex[a] ? Depth : 0f;
                float trimEnd = Depth;
                runTrim.Add(new Vector2(trimStart, trimEnd));
            }
        }

        int pick = 0;
        for (int r = 0; r < runs.Count; r++)
        {
            var run = runs[r];
            var cum = new List<float> { 0f };
            for (int i = 1; i < run.Count; i++) cum.Add(cum[i - 1] + Vector3.Distance(run[i - 1], run[i]));
            float total = cum[cum.Count - 1];
            float s0 = runTrim[r].x, s1 = total - runTrim[r].y;
            float usable = s1 - s0;
            if (usable < 5f) continue;

            var chosen = new List<GameObject>();
            var widths = new List<float>();
            float sum = 0f;
            while (true)
            {
                var p = rows[pick % rows.Count];
                float w = NativeWidth(p);
                if (chosen.Count > 0 && sum + w * 0.5f > usable) break;
                chosen.Add(p);
                widths.Add(w);
                sum += w;
                pick++;
                if (sum >= usable) break;
            }
            float scale = usable / sum;
            float s = s0;
            for (int i = 0; i < chosen.Count; i++)
            {
                float w = widths[i] * scale;
                Vector3 a = PointAt(run, cum, s), b = PointAt(run, cum, s + w);
                Vector3 t = (b - a).normalized;
                Vector3 inward = LeftNormal(t);
                slots.Add(new Slot
                {
                    center = (a + b) * 0.5f + inward * (Depth * 0.5f),
                    tangent = t,
                    outward = -inward,
                    width = Vector3.Distance(a, b) + 0.15f,
                    depth = Depth,
                    prefab = chosen[i]
                });
                s += w;
            }
        }
        return slots;
    }

    static Vector3 PointAt(List<Vector3> run, List<float> cum, float s)
    {
        for (int i = 1; i < run.Count; i++)
            if (s <= cum[i] || i == run.Count - 1)
            {
                float seg = cum[i] - cum[i - 1];
                float t = seg < 1e-4f ? 0f : Mathf.Clamp01((s - cum[i - 1]) / seg);
                return Vector3.Lerp(run[i - 1], run[i], t);
            }
        return run[run.Count - 1];
    }

    static readonly Dictionary<GameObject, float> _nativeWidth = new Dictionary<GameObject, float>();

    static float NativeWidth(GameObject prefab)
    {
        float w;
        if (_nativeWidth.TryGetValue(prefab, out w)) return w;
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.transform.rotation = Quaternion.Euler(0f, YawOffset(inst), 0f);
        w = Span(inst, inst.transform.right);
        Object.DestroyImmediate(inst);
        _nativeWidth[prefab] = w;
        return w;
    }

    static float YawOffset(GameObject go)
    {
        var f = go.GetComponent<BuildingFacing>();
        return f != null ? f.yawOffset : 0f;
    }

    static GameObject Fit(Slot s, Transform parent)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(s.prefab);
        Undo.RegisterCreatedObjectUndo(inst, "Euro block");
        inst.name = (s.corner ? "Corner_" : "Row_") + s.prefab.name;
        inst.transform.SetParent(parent, true);
        Vector3 native = s.prefab.transform.localScale;
        inst.transform.localScale = native;
        float yaw = Mathf.Atan2(s.outward.x, s.outward.z) * Mathf.Rad2Deg + YawOffset(inst);
        inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        float spanW = Mathf.Max(0.5f, Span(inst, s.tangent));
        float spanD = Mathf.Max(0.5f, Span(inst, s.outward));
        float fw = s.width / spanW, fd = s.depth / spanD;
        bool xAlongTangent = Mathf.Abs(Vector3.Dot(inst.transform.right, s.tangent)) > 0.7f;
        float fx = xAlongTangent ? fw : fd;
        float fz = xAlongTangent ? fd : fw;
        inst.transform.localScale = new Vector3(native.x * fx, native.y * fw, native.z * fz);
        Ground(inst, s.center);
        return inst;
    }

    // Same eaves height along the block; corners a bit taller. Vertical stretch is clamped so floors stay plausible.
    static void UnifyHeights(List<GameObject> placed, List<float> natural, List<Slot> slots)
    {
        var rowHeights = new List<float>();
        for (int i = 0; i < placed.Count; i++) if (!slots[i].corner) rowHeights.Add(natural[i]);
        if (rowHeights.Count == 0) rowHeights.AddRange(natural);
        rowHeights.Sort();
        float target = rowHeights[rowHeights.Count / 2];
        for (int i = 0; i < placed.Count; i++)
        {
            float want = slots[i].corner ? target * 1.15f : target;
            float f = Mathf.Clamp(want / Mathf.Max(0.5f, natural[i]), 0.75f, 1.3f);
            var go = placed[i];
            var sc = go.transform.localScale;
            go.transform.localScale = new Vector3(sc.x, sc.y * f, sc.z);
            Ground(go, slots[i].center);
        }
    }

    static void Ground(GameObject go, Vector3 center)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            Bounds b = WorldBounds(go);
            go.transform.position += new Vector3(center.x - b.center.x, GroundY - b.min.y, center.z - b.center.z);
        }
        var f = go.GetComponent<BuildingFacing>();
        if (f != null) go.transform.position += Vector3.up * f.groundOffset;
    }

    static void HideExisting(bool[] mask, EuroBlockRecord record)
    {
        var parent = GameObject.Find("IsraeliBuildings");
        if (parent == null) return;
        foreach (Transform t in parent.transform)
        {
            var go = t.gameObject;
            if (!go.activeSelf) continue;
            string n = go.name;
            if (n.Contains("Playground") || n.Contains("Gas")) continue;
            var rs = go.GetComponentsInChildren<Renderer>();
            Vector3 c = rs.Length > 0 ? WorldBounds(go).center : t.position;
            int cell = Cell(c.x, c.z);
            if (cell < 0 || !mask[cell]) continue;
            Undo.RecordObject(go, "Euro block hide");
            go.SetActive(false);
            record.hidden.Add(go);
        }
    }

    static void BuildCourtyard(bool[] mask, Transform parent)
    {
        // Distance (in cells) from the block edge; courtyard = beyond sidewalk + building depth.
        var dist = EdgeDistance(mask);
        BuildCourtyardMesh(mask, dist, parent);
    }

    static int[] EdgeDistance(bool[] mask)
    {
        var dist = new int[NX * NZ];
        var q = new Queue<int>();
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) { dist[i] = 0; continue; }
            dist[i] = int.MaxValue;
            int ix = i % NX;
            bool edge = ix == 0 || ix == NX - 1 || i < NX || i >= NX * (NZ - 1) ||
                        !mask[i - 1] || !mask[i + 1] || !mask[i - NX] || !mask[i + NX];
            if (edge) { dist[i] = 1; q.Enqueue(i); }
        }
        while (q.Count > 0)
        {
            int p = q.Dequeue();
            int px = p % NX;
            int[] nb = { px > 0 ? p - 1 : -1, px < NX - 1 ? p + 1 : -1, p - NX, p + NX };
            foreach (int nIdx in nb)
            {
                if (nIdx < 0 || nIdx >= mask.Length || !mask[nIdx]) continue;
                if (dist[nIdx] <= dist[p] + 1) continue;
                dist[nIdx] = dist[p] + 1;
                q.Enqueue(nIdx);
            }
        }
        return dist;
    }

    static void BuildCourtyardMesh(bool[] mask, int[] dist, Transform parent)
    {
        int threshold = Mathf.CeilToInt(Sidewalk + Depth + 1f);
        var verts = new List<Vector3>();
        var tris = new List<int>();
        float y = GroundY + 0.04f;
        for (int iz = 0; iz < NZ; iz++)
        {
            int ix = 0;
            while (ix < NX)
            {
                int p = iz * NX + ix;
                if (!mask[p] || dist[p] < threshold) { ix++; continue; }
                int start = ix;
                while (ix < NX && mask[iz * NX + ix] && dist[iz * NX + ix] >= threshold) ix++;
                float xa = X0 + start, xb = X0 + ix, za = Z0 + iz, zb = za + 1f;
                int v0 = verts.Count;
                verts.Add(new Vector3(xa, y, za));
                verts.Add(new Vector3(xa, y, zb));
                verts.Add(new Vector3(xb, y, zb));
                verts.Add(new Vector3(xb, y, za));
                tris.AddRange(new[] { v0, v0 + 1, v0 + 2, v0, v0 + 2, v0 + 3 });
            }
        }
        if (verts.Count == 0) return;

        var go = new GameObject("Courtyard");
        Undo.RegisterCreatedObjectUndo(go, "Euro block");
        go.transform.SetParent(parent, false);
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;
        var mesh = new Mesh { name = "Courtyard_" + parent.name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        var uv = new List<Vector2>(verts.Count);
        foreach (var v in verts) uv.Add(new Vector2(v.x * 0.25f, v.z * 0.25f));
        mesh.SetUVs(0, uv);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/IsraeliStreet/Materials/ParkGrass.mat");
    }

    // ---------- helpers ----------

    static GameObject EnsureRoot()
    {
        var root = GameObject.Find("EuroBlocks");
        if (root != null) return root;
        root = new GameObject("EuroBlocks");
        Undo.RegisterCreatedObjectUndo(root, "Euro block");
        var city = GameObject.Find("SceneCity");
        if (city != null) root.transform.SetParent(city.transform, true);
        return root;
    }

    static GameObject Load(string name) { return AssetDatabase.LoadAssetAtPath<GameObject>(TemplateDir + name + ".prefab"); }

    static float Span(GameObject building, Vector3 axis)
    {
        axis.y = 0f;
        axis.Normalize();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var r in building.GetComponentsInChildren<Renderer>())
        {
            Bounds lb = r.localBounds;
            Vector3 c = lb.center, e = lb.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 wp = r.transform.TransformPoint(c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z));
                float d = wp.x * axis.x + wp.z * axis.z;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
        return min > max ? 0f : max - min;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }
}
