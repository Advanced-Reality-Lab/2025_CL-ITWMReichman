using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Street furniture for the European blocks: bus stops on long street edges, palms along the
// sidewalks, trees and a playground in each courtyard park. Everything lives under the block
// objects so Tools/Euro Blocks/Revert All removes it as well.
public static partial class EuroBlockLayout
{
    const string DressingName = "Dressing";
    const string OldStopsName = "HiddenOldBusStops";
    const int CourtyardDist = 16;          // matches BuildCourtyard threshold (Sidewalk + Depth + 1)
    const float StopSpacing = 75f;
    const float StreetTreeSpacing = 24f;

    static readonly string[] PalmPaths =
    {
        "Assets/IsraeliStreet/_Templates/Tree_PalmTree_1.prefab",
        "Assets/IsraeliStreet/_Templates/Tree_PalmTree_2.prefab",
        "Assets/IsraeliStreet/_Templates/Tree_PalmTree_3.prefab",
        "Assets/IsraeliStreet/_Templates/Tree_PalmTree_4.prefab",
        "Assets/IsraeliStreet/_Templates/Tree_PalmTree_5.prefab"
    };
    static readonly string[] StopPaths =
    {
        "Assets/IsraeliStreet/_Templates/BusStop_BusStop_1.prefab",
        "Assets/IsraeliStreet/_Templates/BusStop_BusStop_2.prefab",
        "Assets/IsraeliStreet/_Templates/BusStop_BusStop_3.prefab",
        "Assets/IsraeliStreet/_Templates/BusStop_BusStop_4.prefab"
    };
    const string AuraDir = "Assets/Aura Landscape/Prefabs/";
    static readonly string[] WalkerPaths = { "Assets/Walking.fbx", "Assets/Walking (1).fbx" };
    const float WalkerSpacing = 100f;   // metres of sidewalk per pedestrian (each model is ~50k triangles)
    const float WalkerScale = 0.82f;

    static void SpawnWalker(GameObject model, Transform parent, Vector3[] path, float startDist, bool reverse, float speed)
    {
        var root = new GameObject("Pedestrian");
        Undo.RegisterCreatedObjectUndo(root, "Euro dressing");
        root.transform.SetParent(parent, false);
        var walker = root.AddComponent<CityWalker>();
        walker.path = path;
        walker.speed = speed;
        walker.reverse = reverse;
        walker.distance = startDist;
        Vector3 dir;
        root.transform.position = walker.Sample(startDist, out dir);
        if (reverse) dir = -dir;
        root.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-4f ? dir : Vector3.forward, Vector3.up);

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
        Undo.RegisterCreatedObjectUndo(inst, "Euro dressing");
        inst.transform.SetParent(root.transform, false);
        inst.transform.localPosition = Vector3.zero;
        inst.transform.localRotation = Quaternion.identity;
        inst.transform.localScale = Vector3.one * WalkerScale;
        var rs = inst.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0)
        {
            Bounds bb = rs[0].bounds;
            foreach (var r in rs) bb.Encapsulate(r.bounds);
            inst.transform.position += Vector3.up * (GroundY - bb.min.y);
        }
    }

    class BlockInfo
    {
        public string name;
        public bool[] mask;
        public int[] dist;
        public List<Vector3> poly;
        public Transform block;
    }

    [MenuItem("Tools/Euro Blocks/Dress All (bus stops, trees, playgrounds)")]
    public static void DressAllMenu() { Debug.Log(DressAll()); }

    public static string DressAll()
    {
        var root = GameObject.Find("EuroBlocks");
        if (root == null) return "run Apply All first";
        ClearDressing(root.transform);

        var kind = BuildKindGrid();
        var blocks = CollectBlocks(kind, root.transform);

        var palms = new List<GameObject>();
        foreach (var p in PalmPaths) palms.Add(AssetDatabase.LoadAssetAtPath<GameObject>(p));
        var stops = new List<GameObject>();
        foreach (var p in StopPaths) stops.Add(AssetDatabase.LoadAssetAtPath<GameObject>(p));
        if (palms.Contains(null) || stops.Contains(null)) return "missing tree / bus stop prefab";

        // Old stops were placed for the previous layout and now stand inside buildings.
        var oldHolder = new GameObject(OldStopsName);
        Undo.RegisterCreatedObjectUndo(oldHolder, "Euro dressing");
        oldHolder.transform.SetParent(root.transform, false);
        var oldRec = oldHolder.AddComponent<EuroBlockRecord>();
        var oldStops = GameObject.Find("IsraeliBusStops");
        if (oldStops != null)
            foreach (Transform t in oldStops.transform)
                if (t.gameObject.activeSelf) { Undo.RecordObject(t.gameObject, "Euro dressing"); t.gameObject.SetActive(false); oldRec.hidden.Add(t.gameObject); }

        var walkers = new List<GameObject>();
        foreach (var p in WalkerPaths)
        {
            var w = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (w != null) walkers.Add(w);
        }

        int nStops = 0, nStreetTrees = 0, nParkTrees = 0, nPlay = 0, nHiddenKept = 0, nWalkers = 0;
        var stopPositions = new List<Vector3>();

        // Bus stops: longest street-facing edge per block, spaced out across the city.
        var candidates = new List<KeyValuePair<float, object[]>>();
        foreach (var b in blocks)
        {
            int n = b.poly.Count;
            float bestLen = 0f;
            object[] best = null;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = b.poly[i], c = b.poly[(i + 1) % n];
                float len = Vector3.Distance(a, c);
                if (len < 28f || len <= bestLen) continue;
                Vector3 t = (c - a) / len;
                Vector3 inward = LeftNormal(t);
                Vector3 mid = (a + c) * 0.5f;
                int roadCell = Cell(mid.x - inward.x * 4f, mid.z - inward.z * 4f);
                if (roadCell < 0 || kind[roadCell] != 2) continue;
                bestLen = len;
                best = new object[] { b, mid, t, inward };
            }
            if (best != null) candidates.Add(new KeyValuePair<float, object[]>(bestLen, best));
        }
        candidates.Sort((x, y) => y.Key.CompareTo(x.Key));
        var stopsPerBlock = new Dictionary<BlockInfo, List<Vector3>>();
        foreach (var cand in candidates)
        {
            var b = (BlockInfo)cand.Value[0];
            var mid = (Vector3)cand.Value[1];
            var t = (Vector3)cand.Value[2];
            var inward = (Vector3)cand.Value[3];
            bool far = true;
            foreach (var s in stopPositions) if (Vector3.Distance(s, mid) < StopSpacing) { far = false; break; }
            if (!far) continue;
            var go = Place(stops[nStops % stops.Count], Dressing(b.block), "BusStop", mid + inward * 1.6f, -inward, 0f, 1.5f);
            if (go == null) continue;
            stopPositions.Add(mid);
            List<Vector3> l;
            if (!stopsPerBlock.TryGetValue(b, out l)) stopsPerBlock[b] = l = new List<Vector3>();
            l.Add(mid);
            nStops++;
        }

        foreach (var b in blocks)
        {
            var dress = Dressing(b.block);
            nHiddenKept += HideKeptUnderRow(b);

            // Street trees along the sidewalk, 1 m in from the curb, clear of corners and stops.
            var line = Offset(b.poly, 1.0f);
            int n = line.Count;
            List<Vector3> myStops;
            stopsPerBlock.TryGetValue(b, out myStops);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = line[i], c = line[(i + 1) % n];
                float len = Vector3.Distance(a, c);
                float margin = 7f;
                if (len < margin * 2f + 1f) continue;
                int count = Mathf.FloorToInt((len - margin * 2f) / StreetTreeSpacing) + 1;
                float step = count > 1 ? (len - margin * 2f) / (count - 1) : 0f;
                float start = count > 1 ? margin : len * 0.5f;
                for (int k = 0; k < count; k++)
                {
                    Vector3 p = Vector3.Lerp(a, c, (start + step * k) / len);
                    bool nearStop = false;
                    if (myStops != null) foreach (var s in myStops) if (Vector3.Distance(s, p) < 7f) nearStop = true;
                    if (nearStop) continue;
                    float h = Hash(p.x, p.z);
                    Place(palms[Pick(h, 997, palms.Count)], dress, "StreetTree", p, Vector3.forward, h * 360f, 1.35f + h * 0.3f);
                    nStreetTrees++;
                }
            }

            // Pedestrians walking the sidewalk loop around the block, between the trees and the facades.
            if (walkers.Count > 0)
            {
                var loop = Offset(b.poly, 1.9f);
                float perim = 0f;
                for (int i = 0; i < loop.Count; i++) perim += Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]);
                int count = Mathf.FloorToInt(perim / WalkerSpacing);
                if (count == 0 && perim > 80f) count = 1;
                var path = new Vector3[loop.Count];
                for (int i = 0; i < loop.Count; i++) path[i] = new Vector3(loop[i].x, GroundY, loop[i].z);
                for (int k = 0; k < count; k++)
                {
                    float h = Hash(perim * 0.13f + k * 7.31f, b.poly[0].x + k);
                    SpawnWalker(walkers[(nWalkers + k) % walkers.Count], dress, path, perim * (k + h * 0.5f) / count, h > 0.5f, 1.1f + h * 0.4f);
                    nWalkers++;
                }
            }

            // Courtyard park: playground at the most interior point, trees around it.
            int best = -1, bestD = 0, area = 0;
            for (int i = 0; i < b.dist.Length; i++)
            {
                if (!b.mask[i] || b.dist[i] < CourtyardDist) continue;
                area++;
                if (b.dist[i] > bestD) { bestD = b.dist[i]; best = i; }
            }
            Vector3 play = new Vector3(float.NaN, 0f, 0f);
            if (best >= 0 && area >= 350 && bestD >= CourtyardDist + 6 && !HasKeptPlayground(b))
            {
                play = CellCenter(best % NX, best / NX);
                BuildPlayground(dress, play, Hash(play.x, play.z) * 360f);
                nPlay++;
            }
            for (int iz = 0; iz < NZ; iz += 12)
                for (int ix = 0; ix < NX; ix += 12)
                {
                    float jx = (Hash(ix, iz) - 0.5f) * 8f, jz = (Hash(iz, ix) - 0.5f) * 8f;
                    if (Hash(ix * 0.37f, iz * 1.91f) < 0.3f) continue;
                    int cell = Cell(X0 + ix + 0.5f + jx, Z0 + iz + 0.5f + jz);
                    if (cell < 0 || !b.mask[cell] || b.dist[cell] < CourtyardDist + 3) continue;
                    Vector3 p = new Vector3(X0 + ix + 0.5f + jx, 0f, Z0 + iz + 0.5f + jz);
                    if (!float.IsNaN(play.x) && Vector3.Distance(p, play) < 10f) continue;
                    float h = Hash(p.x * 1.7f, p.z * 0.6f);
                    Place(palms[Pick(h, 991, palms.Count)], dress, "ParkTree", p, Vector3.forward, h * 360f, 1.6f + h * 0.6f);
                    nParkTrees++;
                }
        }

        return "bus stops " + nStops + " (old hidden " + oldRec.hidden.Count + "), street trees " + nStreetTrees +
               ", park trees " + nParkTrees + ", playgrounds " + nPlay + ", pedestrians " + nWalkers + ", kept items hidden under rows " + nHiddenKept;
    }

    static List<BlockInfo> CollectBlocks(byte[] kind, Transform root)
    {
        var list = new List<BlockInfo>();
        var seen = new bool[NX * NZ];
        var holeSeen = new bool[NX * NZ];
        for (int i = 0; i < kind.Length; i++)
        {
            bool[] mask = null;
            string prefix = null;
            if (kind[i] == 1 && !seen[i]) { mask = Flood(kind, i); prefix = "Block_"; }
            else if (kind[i] == 0 && !holeSeen[i])
            {
                bool border;
                mask = FloodKind(kind, i, 0, holeSeen, out border);
                if (border) continue;
                prefix = "Open_";
            }
            if (mask == null) continue;
            int area = 0;
            double sx = 0, sz = 0;
            for (int j = 0; j < mask.Length; j++)
                if (mask[j]) { if (prefix == "Block_") seen[j] = true; area++; sx += j % NX; sz += j / NX; }
            string name = prefix + Mathf.RoundToInt(X0 + (float)(sx / area)) + "_" + Mathf.RoundToInt(Z0 + (float)(sz / area));
            var block = root.Find(name);
            if (block == null) continue;
            Smooth(mask);
            var ring = Trace(mask);
            if (ring.Count < 16) continue;
            var poly = SimplifyClosed(ring, 1.2f);
            MergeShortEdges(poly, 6f);
            if (SignedArea(poly) < 0f) poly.Reverse();
            list.Add(new BlockInfo { name = name, mask = mask, dist = EdgeDistance(mask), poly = poly, block = block });
        }
        return list;
    }

    static void ClearDressing(Transform root)
    {
        var old = root.Find(OldStopsName);
        if (old != null) RevertBlock(old.gameObject);
        foreach (Transform b in root)
        {
            var d = b.Find(DressingName);
            if (d == null) continue;
            var rec = d.GetComponent<EuroBlockRecord>();
            if (rec != null)
                foreach (var h in rec.hidden)
                    if (h != null) { Undo.RecordObject(h, "Euro dressing"); h.SetActive(true); }
            Undo.DestroyObjectImmediate(d.gameObject);
        }
    }

    static Transform Dressing(Transform block)
    {
        var d = block.Find(DressingName);
        if (d != null) return d;
        var go = new GameObject(DressingName);
        Undo.RegisterCreatedObjectUndo(go, "Euro dressing");
        go.transform.SetParent(block, false);
        go.AddComponent<EuroBlockRecord>();
        return go.transform;
    }

    // Playground / gas-station objects were kept when the block was rebuilt; hide the ones now under the row.
    static int HideKeptUnderRow(BlockInfo b)
    {
        var parent = GameObject.Find("IsraeliBuildings");
        if (parent == null) return 0;
        var rec = Dressing(b.block).GetComponent<EuroBlockRecord>();
        int n = 0;
        foreach (Transform t in parent.transform)
        {
            if (!t.gameObject.activeSelf) continue;
            if (!(t.name.Contains("Playground") || t.name.Contains("Gas"))) continue;
            int cell = Cell(t.position.x, t.position.z);
            if (cell < 0 || !b.mask[cell] || b.dist[cell] >= CourtyardDist) continue;
            Undo.RecordObject(t.gameObject, "Euro dressing");
            t.gameObject.SetActive(false);
            rec.hidden.Add(t.gameObject);
            n++;
        }
        return n;
    }

    static bool HasKeptPlayground(BlockInfo b)
    {
        var parent = GameObject.Find("IsraeliBuildings");
        if (parent == null) return false;
        foreach (Transform t in parent.transform)
        {
            if (!t.gameObject.activeSelf || !t.name.Contains("Playground")) continue;
            int cell = Cell(t.position.x, t.position.z);
            if (cell >= 0 && b.mask[cell]) return true;
        }
        return false;
    }

    static void BuildPlayground(Transform parent, Vector3 c, float yaw)
    {
        var holder = new GameObject("Playground");
        Undo.RegisterCreatedObjectUndo(holder, "Euro dressing");
        holder.transform.SetParent(parent, false);
        holder.transform.position = new Vector3(c.x, GroundY, c.z);
        holder.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        var q = holder.transform.rotation;

        // Sand pad under the equipment.
        var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(pad, "Euro dressing");
        pad.name = "SandPad";
        UnityEngine.Object.DestroyImmediate(pad.GetComponent<Collider>());
        pad.transform.SetParent(holder.transform, false);
        pad.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        pad.transform.localScale = new Vector3(18f, 0.03f, 18f);
        var sand = AssetDatabase.LoadAssetAtPath<Material>("Assets/IsraeliStreet/Materials/PlaygroundSand.mat");
        if (sand == null)
        {
            sand = new Material(Shader.Find("Standard")) { color = new Color(0.86f, 0.76f, 0.55f) };
            AssetDatabase.CreateAsset(sand, "Assets/IsraeliStreet/Materials/PlaygroundSand.mat");
        }
        pad.GetComponent<Renderer>().sharedMaterial = sand;

        var colorful = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/IsraeliStreet/_Templates/Bld_Playground.prefab");
        if (colorful != null)
            Place(colorful, holder.transform, "ColorfulPlayground", c, Vector3.forward, yaw,
                  1.7f / Mathf.Max(0.01f, colorful.transform.localScale.x));
        var bench = AssetDatabase.LoadAssetAtPath<GameObject>(AuraDir + "bench_02.prefab");
        if (bench != null)
        {
            Place(bench, holder.transform, "Bench", c + q * new Vector3(0f, 0f, 9.6f), Vector3.forward, yaw + 180f, 1f);
            Place(bench, holder.transform, "Bench", c + q * new Vector3(0f, 0f, -9.6f), Vector3.forward, yaw, 1f);
        }
    }

    // Instantiates a prefab with its base on the ground, centred on p. outward gives the facing (local +Z),
    // extraYaw is added on top; scaleMul multiplies the prefab's native scale.
    static GameObject Place(GameObject prefab, Transform parent, string name, Vector3 p, Vector3 outward, float extraYaw, float scaleMul)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        if (inst == null) return null;
        Undo.RegisterCreatedObjectUndo(inst, "Euro dressing");
        inst.name = name;
        inst.transform.SetParent(parent, true);
        inst.transform.localScale = prefab.transform.localScale * scaleMul;
        float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + extraYaw;
        inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        inst.transform.position = new Vector3(p.x, GroundY, p.z);
        if (inst.GetComponentsInChildren<Renderer>().Length > 0) Ground(inst, p);
        return inst;
    }

    static int Pick(float h, int mul, int count)
    {
        int i = (int)(h * mul) % count;
        return i < 0 ? i + count : i;
    }

    static float Hash(float x, float z)
    {
        float h = Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.5453f;
        h -= Mathf.Floor(h);
        return float.IsNaN(h) || h < 0f || h >= 1f ? 0.5f : h;
    }
}
