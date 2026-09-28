using System.Collections.Generic;
using UnityEngine;

public enum BlockType { Playground, Parking, GasStation }

[ExecuteAlways]
public class ProceduralStreet : MonoBehaviour
{
    public const float RoadEdgeZ = 3.6f; // 3.6m per lane — wide enough for a car

    public Transform driver;
    public GameObject roadTilePrefab;
    public GameObject[] buildingPrefabs;
    public GameObject[] treePrefabs;
    public GameObject[] benchPrefabs;
    public GameObject[] busStopPrefabs;
    public GameObject[] playgroundPrefabs;
    public GameObject[] gasStationPrefabs;
    public GameObject trashCanPrefab;
    public int supermarketEveryNChunks = 5;
    public int catsPerChunk = 1;
    public int electricityPoleEveryNTiles = 5;
    public int parkingZoneEveryNChunks = 3;
    public GameObject[] parkingCarPrefabs;

    public float tileLength = 10f;
    public int tilesPerChunk = 10;
    public int chunksAhead = 3;
    public int chunksBehind = 1;
    public float lotHalfWidth = 16.5f;
    public float sidewalkZ = 6.2f;
    public float roadY = 0.15f;
    public float lotMargin = 1.5f;
    public int playgroundEveryNChunks = 2;
    public int gasStationEveryNChunks = 10;
    public int parkEveryNChunks = 3;
    public GameObject catPrefab;
    public GameObject[] pedestrianPrefabs;
    public int pedestriansPerChunk = 3;
    public GameObject billboardPrefab;
    [Tooltip("Ten Bis / Clalit posters pasted on some building walls.")]
    public Texture[] billboardAds;
    [Tooltip("1 = one sign every N chunks (100m each).")]
    public int billboardEveryNChunks = 2;
    public int billboardsPerChunk = 1;
    public int seedOffset = 1337;
    public bool showEditorPreview = true;

    [Header("Block System")]
    [Tooltip("Drive Length — how many blocks total. Street hard-stops after the last block.")]
    public int driveLength = 3;
    [Tooltip("How many chunks (100 m each) make up one block.")]
    public int chunksPerBlock = 5;

    private readonly Dictionary<int, GameObject> _spawned = new Dictionary<int, GameObject>();
    private int _lastChunk = int.MinValue;
    private BlockType[] _blockSequence;
    private int _builtSeqSeed = int.MinValue;
    private int _builtDriveLength;

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            // Meshy buildings are 0.3–1.2M verts each. Keep shadow maps short
            // or the GPU spends the whole frame redrawing the city for shadows.
            QualitySettings.shadowDistance = 35f;
            QualitySettings.shadowCascades = 2;
            QualitySettings.pixelLightCount = 1;
        }
        if (Application.isPlaying || showEditorPreview) RefreshChunks();
    }

    void OnDisable()
    {
        ClearAll();
    }

    void Update()
    {
        if (!Application.isPlaying && !showEditorPreview) { ClearAll(); return; }
        if (driver == null) return;
        int cur = Mathf.FloorToInt(driver.position.x / (tileLength * tilesPerChunk));
        if (cur == _lastChunk) return;
        _lastChunk = cur;
        RefreshChunks();
    }

    [ContextMenu("Rebuild Preview")]
    public void Rebuild()
    {
        _blockSequence = null;
        ClearAll();
        RefreshChunks();
    }

    [ContextMenu("Randomize Block Order")]
    public void RandomizeBlocks()
    {
        seedOffset = UnityEngine.Random.Range(0, 99999);
        Rebuild();
    }

    [ContextMenu("Print Block Order")]
    public void PrintBlockOrder()
    {
        _blockSequence = null;
        BuildBlockSequence();
        var sb = new System.Text.StringBuilder();
        sb.Append("Block order (Drive Length=" + driveLength + ", seed=" + seedOffset + "):\n");
        for (int i = 0; i < _blockSequence.Length; i++)
            sb.Append("  Block " + (i + 1) + ": " + _blockSequence[i] + "\n");
        Debug.Log(sb.ToString());
    }

    void OnValidate() { _blockSequence = null; }

    void RefreshChunks()
    {
        int cur = driver == null ? 0 : Mathf.FloorToInt(driver.position.x / (tileLength * tilesPerChunk));
        int maxChunk = Mathf.Max(1, driveLength) * Mathf.Max(1, chunksPerBlock) - 1;
        int lo = Mathf.Max(0, cur - chunksBehind);
        int hi = Mathf.Min(cur + chunksAhead, maxChunk);
        for (int i = lo; i <= hi; i++)
        {
            if (!_spawned.ContainsKey(i) || _spawned[i] == null) SpawnChunk(i);
        }
        var toRemove = new List<int>();
        foreach (var kv in _spawned)
        {
            if (kv.Key < lo || kv.Key > hi) toRemove.Add(kv.Key);
        }
        foreach (var k in toRemove) DestroyChunk(k);
    }

    void ClearAll()
    {
        foreach (var kv in _spawned)
        {
            if (kv.Value != null)
            {
                if (Application.isPlaying) Destroy(kv.Value);
                else DestroyImmediate(kv.Value);
            }
        }
        _spawned.Clear();
        _lastChunk = int.MinValue;
    }

    void DestroyChunk(int idx)
    {
        if (_spawned.TryGetValue(idx, out var go) && go != null)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
        _spawned.Remove(idx);
    }

    void SpawnChunk(int idx)
    {
        var chunk = new GameObject("Chunk_" + idx);
        chunk.transform.SetParent(transform, false);
        chunk.hideFlags = HideFlags.DontSave;
        var rng = new System.Random(idx * 7919 + seedOffset);
        float chunkStartX = idx * tileLength * tilesPerChunk;

        BlockType blockType = GetBlockType(idx);
        bool hasLot = blockType == BlockType.Parking;
        bool hasParkingZone = hasLot;
        // Alternate parking side with each successive block index
        int pzSideDet = (Mathf.FloorToInt((float)idx / Mathf.Max(1, chunksPerBlock))) % 2 == 0 ? -1 : 1;
        int lotSide = -pzSideDet;

        // Carriageway is the overlay slab; skip the original 10m tiles (hidden
        // meshes were still thousands of transforms for no visible gain).

        bool placePlayground = blockType == BlockType.Playground && playgroundPrefabs != null && playgroundPrefabs.Length > 0;
        bool placeGasStation = blockType == BlockType.GasStation && gasStationPrefabs != null && gasStationPrefabs.Length > 0;

        int playgroundTile = rng.Next(tilesPerChunk);
        int playgroundSide = rng.NextDouble() < 0.5 ? -1 : 1;
        int gasTile = rng.Next(tilesPerChunk);
        int gasSide = rng.NextDouble() < 0.5 ? -1 : 1;
        if (placePlayground && placeGasStation && playgroundTile == gasTile && playgroundSide == gasSide)
        {
            gasSide = -gasSide;
        }

        var occupied = new HashSet<long>();
        long Slot(int t, int side) => ((long)t << 4) | (side > 0 ? 1L : 0L);
        bool SlotFree(int t, int side, int span)
        {
            for (int dt = -span; dt <= span; dt++)
            {
                if (occupied.Contains(Slot(t + dt, side))) return false;
            }
            return true;
        }
        void Occupy(int t, int side, int span)
        {
            for (int dt = -span; dt <= span; dt++) occupied.Add(Slot(t + dt, side));
        }

        if (placePlayground) Occupy(playgroundTile, playgroundSide, 2);
        if (hasLot)
        {
            for (int t = 2; t <= 8; t++) Occupy(t, lotSide, 0);
        }

        // Spawn the gas station before buildings so we can reserve its real footprint.
        // The Paz lot is ~20×17 m — a thin tile slot is not enough, and background
        // rows at lotHalfWidth+16 still collide with the canopy depth.
        Bounds gasFootprint = default;
        bool hasGasFootprint = false;
        if (placeGasStation)
        {
            var pf = gasStationPrefabs[rng.Next(gasStationPrefabs.Length)];
            var g = InstantiateChild(pf, chunk.transform);
            float facingOffset = 0f;
            var facing = g.GetComponent<BuildingFacing>();
            if (facing != null) facingOffset = facing.yawOffset;
            float baseYaw = (gasSide > 0 ? 180f : 0f) + facingOffset + (float)(rng.NextDouble() * 10.0 - 5.0);
            g.transform.rotation = Quaternion.Euler(0f, baseYaw, 0f) * g.transform.rotation;
            g.transform.position = new Vector3(chunkStartX + gasTile * tileLength, 0f, gasSide * (lotHalfWidth + 1f));
            GroundAlign(g);
            KeepOffRoad(g, gasSide);
            CheapBuilding(g);

            if (TryGetRendererBounds(g, out gasFootprint))
            {
                SpawnGasLotPad(chunk.transform, gasFootprint);
                // Small clearance so facades don't clip the canopy, without emptying the block.
                gasFootprint.Expand(new Vector3(1.5f, 0f, 1.5f));
                hasGasFootprint = true;
                int t0 = Mathf.FloorToInt((gasFootprint.min.x - chunkStartX) / tileLength);
                int t1 = Mathf.CeilToInt((gasFootprint.max.x - chunkStartX) / tileLength);
                for (int t = t0; t <= t1; t++) Occupy(t, gasSide, 0);
            }
            else
            {
                Occupy(gasTile, gasSide, 2);
            }
        }

        var placedBuildings = new List<KeyValuePair<GameObject, int>>();
        if (buildingPrefabs != null && buildingPrefabs.Length > 0)
        {
            // Every 20m, no skips, little jitter — a continuous street wall, not scattered lots.
            const int step = 2;
            for (int t = 0; t < tilesPerChunk; t += step)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!SlotFree(t, side, 0)) continue;

                    var pf = buildingPrefabs[rng.Next(buildingPrefabs.Length)];
                    var b = InstantiateChild(pf, chunk.transform);

                    float facingOffset = 0f;
                    var facing = b.GetComponent<BuildingFacing>();
                    if (facing != null) facingOffset = facing.yawOffset;

                    float baseYaw = (side > 0 ? 180f : 0f) + facingOffset + (float)(rng.NextDouble() * 3.0 - 1.5);
                    b.transform.rotation = Quaternion.Euler(0f, baseYaw, 0f) * b.transform.rotation;

                    float lotZ = side * lotHalfWidth;
                    float xJit = (float)(rng.NextDouble() * 1.2);
                    b.transform.position = new Vector3(chunkStartX + t * tileLength + xJit, 0f, lotZ);

                    GroundAlign(b);
                    KeepOffRoad(b, side);
                    if (hasGasFootprint && OverlapsGasFootprint(b, gasFootprint))
                    {
                        DestroySpawned(b);
                        continue;
                    }
                    CheapBuilding(b);
                    Occupy(t, side, 0);
                    placedBuildings.Add(new KeyValuePair<GameObject, int>(b, side));
                }
            }
        }

        StickWallPosters(placedBuildings, chunk.transform, idx, rng);

        // Background city — two staggered rows of buildings behind the building line, on both sides always
        if (buildingPrefabs != null && buildingPrefabs.Length > 0)
        {
            for (int row = 0; row < 2; row++)
            {
                float bgZ = lotHalfWidth + 16f + row * 16f;
                int bgStep = row == 0 ? 2 : 3;
                float scaleMin = row == 0 ? 1.0f : 1.2f;
                float scaleRange = row == 0 ? 0.6f : 0.8f;
                for (int t = row; t < tilesPerChunk; t += bgStep)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var pf = buildingPrefabs[rng.Next(buildingPrefabs.Length)];
                        var b = InstantiateChild(pf, chunk.transform);
                        var facing = b.GetComponent<BuildingFacing>();
                        float facingOffset = facing != null ? facing.yawOffset : 0f;
                        float baseYaw = (side > 0 ? 180f : 0f) + facingOffset;
                        b.transform.rotation = Quaternion.Euler(0f, baseYaw, 0f) * b.transform.rotation;
                        float xJit = (float)(rng.NextDouble() * 4.0 - 2.0);
                        float zJit = (float)(rng.NextDouble() * 4.0);
                        b.transform.position = new Vector3(chunkStartX + t * tileLength + xJit, 0f, side * (bgZ + zJit));
                        float sc = scaleMin + (float)(rng.NextDouble() * scaleRange);
                        b.transform.localScale *= sc;
                        GroundAlign(b);
                        if (hasGasFootprint && side == gasSide)
                            NudgeOffGasFootprint(b, side, gasFootprint);
                        if (b == null) continue;
                        CheapBuilding(b);
                    }
                }
            }
        }

        if (placePlayground)
        {
            var pf = playgroundPrefabs[rng.Next(playgroundPrefabs.Length)];
            var g = InstantiateChild(pf, chunk.transform);
            float facingOffset = 0f;
            var facing = g.GetComponent<BuildingFacing>();
            if (facing != null) facingOffset = facing.yawOffset;
            float baseYaw = (playgroundSide > 0 ? 180f : 0f) + facingOffset + (float)(rng.NextDouble() * 10.0 - 5.0);
            g.transform.rotation = Quaternion.Euler(0f, baseYaw, 0f) * g.transform.rotation;
            g.transform.position = new Vector3(chunkStartX + playgroundTile * tileLength, 0f, playgroundSide * (sidewalkZ + 4f));
            GroundAlign(g);
            KeepOffRoad(g, playgroundSide);
            CheapBuilding(g);
        }

        bool placePark = parkEveryNChunks > 0 && idx % parkEveryNChunks == 0 && blockType == BlockType.Playground;
        if (placePark)
        {
            int parkSide = (rng.NextDouble() < 0.5 ? -1 : 1);
            if (placePlayground && parkSide == playgroundSide) parkSide = -parkSide;
            float parkX = chunkStartX;
            float parkW = tileLength * tilesPerChunk * 0.7f;
            var parkGo = new GameObject("CityPark_" + idx);
            parkGo.transform.SetParent(chunk.transform, false);
            parkGo.hideFlags = HideFlags.DontSave;
            parkGo.transform.position = new Vector3(parkX, 0f, 0f);
            var park = parkGo.AddComponent<CityPark>();
            park.side = parkSide;
            park.width = parkW;
            park.depth = 14f;
            park.sidewalkEdgeZ = sidewalkZ;
            park.treePrefabs = treePrefabs;
            park.benchPrefabs = benchPrefabs;
            park.Build();

            int numCats = catsPerChunk > 0 ? 1 : 0;
            for (int ci = 0; ci < numCats; ci++)
            {
                float catX = parkX + (float)(rng.NextDouble() * parkW);
                float catZ = parkSide * (sidewalkZ - 0.8f - (float)(rng.NextDouble() * 1.2f));
                SpawnStreetCat(chunk.transform, new Vector3(catX, 0f, catZ), parkSide > 0 ? 90f : -90f, rng);
            }
        }

        if (treePrefabs != null && treePrefabs.Length > 0 && !hasParkingZone)
        {
            var lastTreeTile = new Dictionary<int, int> { { -1, -99 }, { 1, -99 } };
            for (int t = 0; t < tilesPerChunk; t++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (hasLot) continue;
                    if (rng.NextDouble() >= 0.5) continue;
                    if (t - lastTreeTile[side] < 2) continue;
                    lastTreeTile[side] = t;
                    var pf = treePrefabs[rng.Next(treePrefabs.Length)];
                    var g = InstantiateChild(pf, chunk.transform);
                    float xJit = (float)(rng.NextDouble() * 4.0);
                    float zJit = (float)(rng.NextDouble() * 1.5);
                    g.transform.position = new Vector3(chunkStartX + t * tileLength + xJit, 0f, side * (sidewalkZ + zJit));
                    g.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 360.0), 0f) * g.transform.rotation;
                    CheapProp(g);
                }
            }
        }

        if (!hasParkingZone && benchPrefabs != null && benchPrefabs.Length > 0 && rng.NextDouble() < 0.5)
        {
            var pf = benchPrefabs[rng.Next(benchPrefabs.Length)];
            int t = rng.Next(tilesPerChunk);
            int side = rng.NextDouble() < 0.5 ? -1 : 1;
            var g = InstantiateChild(pf, chunk.transform);
            g.transform.position = new Vector3(chunkStartX + t * tileLength, 0f, side * (sidewalkZ - 1f));
            g.transform.rotation = Quaternion.Euler(0f, side > 0 ? 0f : 180f, 0f) * g.transform.rotation;
            GroundAlign(g);
        }

        Material pavementMat = null;
        if (roadTilePrefab != null)
        {
            var r = roadTilePrefab.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                var mats = r.sharedMaterials;
                if (mats.Length > 0) pavementMat = mats[0];
            }
        }
        Material asphaltMat = MakeAsphalt();

        SpawnWideCarriageway(
            chunk.transform, chunkStartX, tileLength * tilesPerChunk, asphaltMat, pavementMat,
            hasParkingZone ? pzSideDet : 0, 0f, hasParkingZone ? 36f : -1f);

        int bsBaySide = 0;
        float bsBayGapStart = -1f, bsBayGapEnd = -1f;

        if (busStopPrefabs != null && busStopPrefabs.Length > 0 && rng.NextDouble() < 0.85)
        {
            var pf = busStopPrefabs[rng.Next(busStopPrefabs.Length)];
            int bsT = rng.Next(tilesPerChunk);
            int bsSide = rng.NextDouble() < 0.5 ? -1 : 1;

            float bayLen = 12f;
            float bayExtra = 3.0f;
            float bayLocalX = Mathf.Max(0f, bsT * tileLength - bayLen * 0.3f);

            bsBaySide = bsSide;
            bsBayGapStart = bayLocalX;
            bsBayGapEnd = bayLocalX + bayLen;

            var bayGo = new GameObject("BusBay_" + idx);
            bayGo.transform.SetParent(chunk.transform, false);
            bayGo.hideFlags = HideFlags.DontSave;
            bayGo.transform.position = new Vector3(chunkStartX + bayLocalX, 0f, 0f);
            var bay = bayGo.AddComponent<BusBay>();
            bay.side = bsSide;
            bay.bayLength = bayLen;
            bay.extraWidth = bayExtra;
            bay.roadEdgeZ = RoadEdgeZ;
            bay.roadSurfaceY = roadY;
            bay.roadSurfaceMaterial = pavementMat;
            bay.Build();

            var bsTreeKill = new List<GameObject>();
            foreach (Transform c in chunk.transform)
            {
                if (c.name.StartsWith("Road_1_line"))
                {
                    foreach (Transform gc in c)
                        if (gc.name.StartsWith("Tree")) bsTreeKill.Add(gc.gameObject);
                }
                else if (c.name.StartsWith("Tree"))
                {
                    if ((bsSide > 0 && c.position.z > 0) || (bsSide < 0 && c.position.z < 0))
                        bsTreeKill.Add(c.gameObject);
                }
            }
            foreach (var kill in bsTreeKill)
            {
                if (Application.isPlaying) Destroy(kill); else DestroyImmediate(kill);
            }

            var g = InstantiateChild(pf, chunk.transform);
            float shelterZ = bsSide * (RoadEdgeZ + 1.6f);
            g.transform.position = new Vector3(chunkStartX + bsT * tileLength, 0f, shelterZ);
            g.transform.rotation = Quaternion.Euler(0f, bsSide > 0 ? 0f : 180f, 0f) * g.transform.rotation;
            GroundAlign(g);
        }

        // GasStation blocks always spawn billboards (more of them); other blocks use the normal cadence.
        bool spawnBillboard = billboardPrefab != null && billboardsPerChunk > 0 && (
            blockType == BlockType.GasStation ||
            (billboardEveryNChunks > 0 && ((idx % billboardEveryNChunks) + billboardEveryNChunks) % billboardEveryNChunks == 0));
        if (idx == 0) spawnBillboard = billboardPrefab != null && billboardsPerChunk > 0;
        if (spawnBillboard)
        {
            int n = Mathf.Max(1, blockType == BlockType.GasStation ? billboardsPerChunk + 2 : billboardsPerChunk);
            float bbChunkLen = tileLength * tilesPerChunk;
            for (int i = 0; i < n; i++)
            {
                int bbSide = rng.NextDouble() < 0.5 ? -1 : 1;
                if (hasParkingZone && bbSide == pzSideDet) bbSide = -bbSide;
                float x;
                if (idx == 0)
                    x = chunkStartX + 8f + (float)rng.NextDouble() * 10f;
                else
                    x = chunkStartX + 20f + (float)rng.NextDouble() * Mathf.Max(20f, bbChunkLen - 40f);
                float z = bbSide * (sidewalkZ + 0.8f);

                var g = InstantiateChild(billboardPrefab, chunk.transform);
                g.name = "Billboard_" + idx + "_" + i;
                g.transform.localScale = Vector3.one * 1.35f;
                g.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                g.transform.position = new Vector3(x, 0f, z);
                GroundAlign(g);
                foreach (var col in g.GetComponentsInChildren<Collider>())
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }
            }
        }

        if (buildingPrefabs != null && buildingPrefabs.Length > 0)
        {
            for (int t = 0; t < tilesPerChunk; t += 3)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!occupied.Contains(((long)t << 4) | (side > 0 ? 1L : 0L))) continue;
                    if (rng.NextDouble() < 0.5f) continue;
                    if (trashCanPrefab == null) continue;
                    var binGo = InstantiateChild(trashCanPrefab, chunk.transform);
                    binGo.name = "TrashBin";
                    float binX = chunkStartX + t * tileLength + (float)(rng.NextDouble() * 2.0);
                    float binZ = side * (sidewalkZ + 0.6f);
                    binGo.transform.position = new Vector3(binX, 0f, binZ);
                    binGo.transform.rotation = Quaternion.Euler(0f, side > 0 ? 160f : 20f, 0f) * binGo.transform.rotation;
                    GroundAlign(binGo);
                }
            }
        }

        float chunkLen = tileLength * tilesPerChunk;
        float pzZoneLen = 36f;
        float lotGapStart = 4f * tileLength;
        float lotGapEnd = 6f * tileLength;

        for (int side = -1; side <= 1; side += 2)
        {
            var curbGo = new GameObject("Curb_" + (side > 0 ? "R" : "L"));
            curbGo.transform.SetParent(chunk.transform, false);
            curbGo.hideFlags = HideFlags.DontSave;
            curbGo.transform.position = new Vector3(chunkStartX, roadY, 0f);
            var stripe = curbGo.AddComponent<CurbStripe>();
            stripe.length = chunkLen;
            stripe.side = side;
            stripe.zOffset = RoadEdgeZ;
            stripe.stripeHeight = 0.16f;
            stripe.stripeWidth = 0.22f;
            stripe.segmentLength = 0.65f;

            if (hasParkingZone && side == pzSideDet)
            {
                stripe.gapStart = 0f;
                stripe.gapEnd = pzZoneLen;
            }
            else if (hasLot && side == lotSide)
            {
                stripe.gapStart = lotGapStart;
                stripe.gapEnd = lotGapEnd;
            }

            if (bsBaySide != 0 && side == bsBaySide)
            {
                float seg = stripe.segmentLength;
                stripe.altStart = Mathf.Floor(bsBayGapStart / seg) * seg;
                stripe.altEnd = Mathf.Ceil(bsBayGapEnd / seg) * seg;
            }

            stripe.Build();
        }

        var trashPositions = new List<Vector3>();
        foreach (Transform c in chunk.transform)
            if (c.name.StartsWith("TrashBin")) trashPositions.Add(c.position);

        int catsThisChunk = Mathf.Max(0, catsPerChunk);
        for (int ci = 0; ci < catsThisChunk; ci++)
        {
            float catX, catZ;
            int catSide = rng.NextDouble() < 0.5 ? -1 : 1;

            if (trashPositions.Count > 0 && rng.NextDouble() < 0.6f)
            {
                var bin = trashPositions[rng.Next(trashPositions.Count)];
                catX = bin.x + (float)(rng.NextDouble() * 1.5 - 0.75);
                catZ = bin.z + (float)(rng.NextDouble() * 0.6 - 0.3);
                catSide = bin.z >= 0 ? 1 : -1;
            }
            else
            {
                int catT = rng.Next(tilesPerChunk);
                catX = chunkStartX + catT * tileLength + (float)(rng.NextDouble() * tileLength * 0.8f);
                // Place cats on the visible sidewalk (between curb and outer edge)
                catZ = catSide * (RoadEdgeZ + 0.8f + (float)(rng.NextDouble() * (sidewalkZ - RoadEdgeZ - 1.2f)));
            }

            SpawnStreetCat(
                chunk.transform,
                new Vector3(catX, roadY, catZ),
                catSide > 0 ? 90f : -90f,
                rng);
        }

        if (pedestrianPrefabs != null && pedestrianPrefabs.Length > 0 && pedestriansPerChunk > 0)
        {
            for (int pi = 0; pi < pedestriansPerChunk; pi++)
            {
                int side = rng.NextDouble() < 0.5 ? -1 : 1;
                float px = chunkStartX + (float)(rng.NextDouble() * tilesPerChunk * tileLength);
                float pz = side * (RoadEdgeZ + 1.0f + (float)(rng.NextDouble() * (sidewalkZ - RoadEdgeZ - 1.4f)));
                float yaw = (rng.NextDouble() < 0.5 ? 90f : 270f) + (float)(rng.NextDouble() * 10.0 - 5.0);
                SpawnPedestrian(chunk.transform, new Vector3(px, roadY, pz), yaw, rng);
            }
        }

        if (electricityPoleEveryNTiles > 0)
        {
            float poleSpacingM = electricityPoleEveryNTiles * tileLength;
            for (int t = 0; t < tilesPerChunk; t += electricityPoleEveryNTiles)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    var poleGo = new GameObject("ElecPole");
                    poleGo.transform.SetParent(chunk.transform, false);
                    poleGo.hideFlags = HideFlags.DontSave;
                    poleGo.transform.position = new Vector3(chunkStartX + t * tileLength, 0f, side * (sidewalkZ + 2.5f));
                    var pole = poleGo.AddComponent<ElectricityPole>();
                    pole.poleSpacing = poleSpacingM;
                    pole.Build();
                }
            }
        }

        if (hasParkingZone)
        {
            int pzSide = pzSideDet;

            var pzGo = new GameObject("ParkingZone_" + idx);
            pzGo.transform.SetParent(chunk.transform, false);
            pzGo.hideFlags = HideFlags.DontSave;
            pzGo.transform.position = new Vector3(chunkStartX, 0f, 0f);
            var pz = pzGo.AddComponent<ParkingZone>();
            pz.zoneLength = 36f;
            pz.side = pzSide;
            pz.curbZ = RoadEdgeZ;
            pz.carPrefabs = parkingCarPrefabs;
            pz.roadSurfaceMaterial = asphaltMat;
            pz.Build();

            var plGo = new GameObject("ParkingLot_" + idx);
            plGo.transform.SetParent(chunk.transform, false);
            plGo.hideFlags = HideFlags.DontSave;
            plGo.transform.position = new Vector3(chunkStartX + tileLength * 4f, 0f, 0f);
            var pl = plGo.AddComponent<ParkingLot>();
            pl.side = -pzSide;
            pl.roadEdgeZ = RoadEdgeZ;
            pl.rows = 2;
            pl.cols = 4;
            pl.carPrefabs = parkingCarPrefabs;
            pl.roadSurfaceMaterial = asphaltMat;
            pl.Build();
        }

        _spawned[idx] = chunk;
    }

    void SpawnStreetCat(Transform parent, Vector3 pos, float yaw, System.Random rng)
    {
        if (catPrefab == null) return;

        var catGo = new GameObject("StreetCat");
        catGo.transform.SetParent(parent, worldPositionStays: false);
        // Place at Y=0 so Build()'s ground-align lands feet at Y=0, then we lift to road surface
        catGo.transform.position = new Vector3(pos.x, 0f, pos.z);
        catGo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        try
        {
            var sc = catGo.AddComponent<StreetCat>();
            sc.catPrefab = catPrefab;
            sc.patrolRange = 3f + (float)(rng.NextDouble() * 4f);
            sc.speed = 0.5f + (float)(rng.NextDouble() * 0.4f);
            sc.sidewalkMinZ = RoadEdgeZ + 0.3f;
            sc.sidewalkMaxZ = sidewalkZ;
            sc.Build();
            // Build() aligns the model feet to Y=0; lift the whole cat up to the road surface
            var cp = catGo.transform.position;
            catGo.transform.position = new Vector3(cp.x, roadY, cp.z);
            CheapProp(catGo);
            Debug.Log("Cat spawned at " + catGo.transform.position);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("StreetCat spawn failed: " + ex.Message);
        }

        SetHideFlagsRecursive(catGo.transform);
    }

    void SpawnPedestrian(Transform parent, Vector3 pos, float yaw, System.Random rng)
    {
        if (pedestrianPrefabs == null || pedestrianPrefabs.Length == 0) return;
        var pf = pedestrianPrefabs[rng.Next(pedestrianPrefabs.Length)];
        if (pf == null) return;

        var go = new GameObject("Pedestrian");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.position = new Vector3(pos.x, 0f, pos.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.hideFlags = HideFlags.DontSave;

        try
        {
            GameObject model;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                model = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(pf, go.transform);
            else
#endif
                model = Instantiate(pf, go.transform);

            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Ground-align: shift up so feet sit at Y=0
            var rends = model.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                var p = model.transform.position;
                model.transform.position = new Vector3(p.x, p.y - b.min.y, p.z);
            }

            model.hideFlags = HideFlags.DontSave;

            // Play legacy animation (Mixamo FBX imported with Legacy rig)
            var legacyAnim = model.GetComponentInChildren<Animation>();
            if (legacyAnim != null) { legacyAnim.wrapMode = WrapMode.Loop; legacyAnim.Play(); }

            ApplyPedestrianClothingColors(model, rng);

            var sp = go.AddComponent<StreetPedestrian>();
            sp.speed = 1.2f + (float)(rng.NextDouble() * 0.4);
            sp.patrolRange = 12f + (float)(rng.NextDouble() * 10f);
            sp.sidewalkMinZ = RoadEdgeZ + 0.5f;
            sp.sidewalkMaxZ = sidewalkZ;
            sp.Build();

            var cp = go.transform.position;
            go.transform.position = new Vector3(cp.x, roadY, cp.z);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Pedestrian spawn failed: " + ex.Message);
        }

        SetHideFlagsRecursive(go.transform);
    }

    static readonly Color[] PedestrianShirtColors = {
        new Color(0.85f, 0.20f, 0.20f), // red
        new Color(0.20f, 0.40f, 0.85f), // blue
        new Color(0.20f, 0.65f, 0.25f), // green
        new Color(0.90f, 0.55f, 0.10f), // orange
        new Color(0.55f, 0.20f, 0.70f), // purple
        new Color(0.15f, 0.15f, 0.15f), // black
        new Color(0.95f, 0.95f, 0.95f), // white
        new Color(0.30f, 0.55f, 0.50f), // teal
    };

    static readonly Color[] PedestrianPantsNeutrals = {
        new Color(0.12f, 0.12f, 0.14f), // black
        new Color(0.12f, 0.16f, 0.28f), // navy
        new Color(0.32f, 0.32f, 0.34f), // gray
        new Color(0.36f, 0.24f, 0.14f), // brown
    };

    static bool PedestrianColorsSimilar(Color a, Color b)
    {
        float dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b;
        return (dr * dr + dg * dg + db * db) < 0.04f;
    }

    // Face / skin / eye / hair must keep original textures (do not tint).
    static bool IsFaceSkinEyeHairName(string key)
    {
        return key.Contains("face") || key.Contains("skin") || key.Contains("head")
            || key.Contains("eye") || key.Contains("hair") || key.Contains("lash")
            || key.Contains("brow") || key.Contains("teeth") || key.Contains("mouth")
            || key.Contains("scalp") || key.Contains("beard");
    }

    static bool IsPantsName(string key)
    {
        return key.Contains("pants") || key.Contains("trouser") || key.Contains("jean");
    }

    static bool IsShirtName(string key)
    {
        return key.Contains("shirt") || key.Contains("top") || key.Contains("coat")
            || key.Contains("jacket") || key.Contains("sweater") || key.Contains("hoodie")
            || key.Contains("torso");
    }

    static void TintRendererSlot(Renderer r, int materialIndex, MaterialPropertyBlock block, Color c)
    {
        r.GetPropertyBlock(block, materialIndex);
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        r.SetPropertyBlock(block, materialIndex);
    }

    void ApplyPedestrianClothingColors(GameObject model, System.Random rng)
    {
        Color shirt = PedestrianShirtColors[rng.Next(PedestrianShirtColors.Length)];

        // Pants: dark neutrals or another palette color, never matching this shirt.
        Color pants;
        int tries = 0;
        do
        {
            if (rng.NextDouble() < 0.65)
                pants = PedestrianPantsNeutrals[rng.Next(PedestrianPantsNeutrals.Length)];
            else
                pants = PedestrianShirtColors[rng.Next(PedestrianShirtColors.Length)];
            tries++;
        } while (PedestrianColorsSimilar(shirt, pants) && tries < 24);
        if (PedestrianColorsSimilar(shirt, pants))
            pants = PedestrianColorsSimilar(shirt, PedestrianPantsNeutrals[1])
                ? PedestrianPantsNeutrals[2]
                : PedestrianPantsNeutrals[1];

        bool hasDedicatedPants = false;
        bool hasDedicatedShirt = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            string goKey = r.gameObject.name.ToLowerInvariant();
            if (IsPantsName(goKey)) hasDedicatedPants = true;
            if (IsShirtName(goKey)) hasDedicatedShirt = true;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                string mk = m.name.ToLowerInvariant();
                if (IsPantsName(mk)) hasDedicatedPants = true;
                if (IsShirtName(mk)) hasDedicatedShirt = true;
            }
        }

        var block = new MaterialPropertyBlock();
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int mi = 0; mi < mats.Length; mi++)
            {
                var mat = mats[mi];
                if (mat == null) continue;
                string key = (mat.name + " " + r.gameObject.name).ToLowerInvariant();
                if (IsFaceSkinEyeHairName(key)) continue;

                if (IsPantsName(key))
                {
                    TintRendererSlot(r, mi, block, pants);
                    continue;
                }
                if (IsShirtName(key))
                {
                    TintRendererSlot(r, mi, block, shirt);
                    continue;
                }

                // Combined body atlas (e.g. Ch12_body): split face / shirt / pants by bone weights.
                // Never tint the whole body material — that paints over the face.
                if (key.Contains("body") && !hasDedicatedPants && !hasDedicatedShirt
                    && r is SkinnedMeshRenderer smr)
                {
                    TrySplitCombinedBodyClothing(smr, shirt, pants);
                    continue;
                }
                // Dedicated body/skin mesh alongside separate shirt/pants (e.g. Ch41_Body): leave untinted.
            }
        }
    }

    static void AccumulateBoneClassWeight(int[] boneClass, int boneIndex, float weight,
        ref float faceW, ref float pantsW, ref float shirtW)
    {
        if (weight <= 0f || boneClass == null || boneClass.Length == 0) return;
        int cls = boneClass[Mathf.Clamp(boneIndex, 0, boneClass.Length - 1)];
        if (cls == 2) faceW += weight;
        else if (cls == 1) pantsW += weight;
        else shirtW += weight;
    }

    // Splits a combined body material into untinted face/skin + tinted shirt + tinted pants.
    // Returns false if the mesh cannot be read.
    static bool TrySplitCombinedBodyClothing(SkinnedMeshRenderer smr, Color shirt, Color pants)
    {
        var src = smr.sharedMesh;
        if (src == null || !src.isReadable) return false;
        if (smr.bones == null || smr.bones.Length == 0) return false;

        var bones = smr.bones;
        var boneClass = new int[bones.Length]; // 0 shirt, 1 pants, 2 face/skin
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) { boneClass[i] = 0; continue; }
            string bn = bones[i].name.ToLowerInvariant();
            if (bn.Contains("head") || bn.Contains("neck") || bn.Contains("eye")
                || bn.Contains("jaw") || bn.Contains("face") || bn.Contains("brow")
                || bn.Contains("cheek") || bn.Contains("nose") || bn.Contains("mouth")
                || bn.Contains("teeth") || bn.Contains("tongue") || bn.Contains("lip"))
                boneClass[i] = 2;
            else if (bn.Contains("upleg") || bn.Contains("leg") || bn.Contains("foot")
                     || bn.Contains("toe") || bn.EndsWith("hips") || bn.Contains(":hips")
                     || bn.Contains("hip") || bn.Contains("butt"))
                boneClass[i] = 1;
            else
                boneClass[i] = 0;
        }

        var weights = src.boneWeights;
        if (weights == null || weights.Length != src.vertexCount) return false;

        // Classify by summed bone weights. Any meaningful head/neck influence keeps the
        // face texture — otherwise spine-weighted cheek/forehead verts get the shirt tint.
        var vertClass = new int[src.vertexCount];
        for (int v = 0; v < src.vertexCount; v++)
        {
            var w = weights[v];
            float faceW = 0f, pantsW = 0f, shirtW = 0f;
            AccumulateBoneClassWeight(boneClass, w.boneIndex0, w.weight0, ref faceW, ref pantsW, ref shirtW);
            AccumulateBoneClassWeight(boneClass, w.boneIndex1, w.weight1, ref faceW, ref pantsW, ref shirtW);
            AccumulateBoneClassWeight(boneClass, w.boneIndex2, w.weight2, ref faceW, ref pantsW, ref shirtW);
            AccumulateBoneClassWeight(boneClass, w.boneIndex3, w.weight3, ref faceW, ref pantsW, ref shirtW);

            if (faceW >= 0.05f) vertClass[v] = 2;
            else if (pantsW >= shirtW && pantsW > 0f) vertClass[v] = 1;
            else vertClass[v] = 0;
        }

        // Bind-pose height: top of the mesh is head/neck — keep those on the face material
        // even when spine bones out-weight the head bone.
        var bindVerts = src.vertices;
        if (bindVerts != null && bindVerts.Length == vertClass.Length)
        {
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int v = 0; v < bindVerts.Length; v++)
            {
                float y = bindVerts[v].y;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            float headCut = Mathf.Lerp(minY, maxY, 0.68f);
            for (int v = 0; v < vertClass.Length; v++)
            {
                if (bindVerts[v].y >= headCut) vertClass[v] = 2;
            }
        }

        var oldMats = smr.sharedMaterials;
        int bodySub = -1;
        Material srcMat = null;
        for (int i = 0; i < oldMats.Length; i++)
        {
            if (oldMats[i] == null) continue;
            if (oldMats[i].name.ToLowerInvariant().Contains("body"))
            {
                bodySub = i;
                srcMat = oldMats[i];
                break;
            }
        }
        if (bodySub < 0)
        {
            bodySub = 0;
            srcMat = oldMats.Length > 0 ? oldMats[0] : null;
        }
        if (srcMat == null) return false;
        bodySub = Mathf.Clamp(bodySub, 0, src.subMeshCount - 1);

        var faceMat = new Material(srcMat) { name = srcMat.name + "_Face" };
        var shirtMat = new Material(srcMat) { name = srcMat.name + "_Shirt" };
        var pantsMat = new Material(srcMat) { name = srcMat.name + "_Pants" };
        // Keep face/skin albedo unmultiplied so Mixamo facial features stay visible.
        if (faceMat.HasProperty("_BaseColor")) faceMat.SetColor("_BaseColor", Color.white);
        if (faceMat.HasProperty("_Color")) faceMat.SetColor("_Color", Color.white);
        if (shirtMat.HasProperty("_BaseColor")) shirtMat.SetColor("_BaseColor", shirt);
        if (shirtMat.HasProperty("_Color")) shirtMat.SetColor("_Color", shirt);
        if (pantsMat.HasProperty("_BaseColor")) pantsMat.SetColor("_BaseColor", pants);
        if (pantsMat.HasProperty("_Color")) pantsMat.SetColor("_Color", pants);

        var faceTris = new List<int>();
        var shirtTris = new List<int>();
        var pantsTris = new List<int>();
        var bodyTris = src.GetTriangles(bodySub);
        for (int t = 0; t < bodyTris.Length; t += 3)
        {
            int a = bodyTris[t], b = bodyTris[t + 1], c = bodyTris[t + 2];
            int ca = vertClass[a], cb = vertClass[b], cc = vertClass[c];
            int vote;
            if (ca == cb || ca == cc) vote = ca;
            else if (cb == cc) vote = cb;
            else if (ca == 2 || cb == 2 || cc == 2) vote = 2;
            else if (ca == 1 || cb == 1 || cc == 1) vote = 1;
            else vote = 0;

            if (vote == 2) { faceTris.Add(a); faceTris.Add(b); faceTris.Add(c); }
            else if (vote == 1) { pantsTris.Add(a); pantsTris.Add(b); pantsTris.Add(c); }
            else { shirtTris.Add(a); shirtTris.Add(b); shirtTris.Add(c); }
        }

        var newMats = new List<Material>();
        var orderedTris = new List<List<int>>();
        for (int i = 0; i < oldMats.Length; i++)
        {
            var m = oldMats[i];
            if (m == null) continue;
            if (i == bodySub)
            {
                if (faceTris.Count > 0) { newMats.Add(faceMat); orderedTris.Add(faceTris); }
                if (shirtTris.Count > 0) { newMats.Add(shirtMat); orderedTris.Add(shirtTris); }
                if (pantsTris.Count > 0) { newMats.Add(pantsMat); orderedTris.Add(pantsTris); }
            }
            else
            {
                int srcSub = Mathf.Min(i, src.subMeshCount - 1);
                newMats.Add(m);
                orderedTris.Add(new List<int>(src.GetTriangles(srcSub)));
            }
        }

        if (orderedTris.Count == 0) return false;

        var mesh = Object.Instantiate(src);
        mesh.name = src.name + "_Clothed";
        mesh.subMeshCount = orderedTris.Count;
        for (int i = 0; i < orderedTris.Count; i++)
            mesh.SetTriangles(orderedTris[i], i, true);

        smr.sharedMesh = mesh;
        smr.sharedMaterials = newMats.ToArray();
        return true;
    }

    void StripBakedStreetFurniture(GameObject road)
    {
        var toDestroy = new List<GameObject>();
        foreach (Transform child in road.transform)
        {
            string n = child.name;
            if (n.StartsWith("Bench_") || n.StartsWith("Trash_can") || n.StartsWith("Tree"))
            {
                toDestroy.Add(child.gameObject);
            }
            else if (n.StartsWith("Road_1_line"))
            {
                var poles = new List<GameObject>();
                foreach (Transform gc in child)
                    if (gc.name.StartsWith("Pole"))
                        poles.Add(gc.gameObject);
                foreach (var p in poles)
                {
                    if (Application.isPlaying) Destroy(p); else DestroyImmediate(p);
                }
            }
        }
        foreach (var go in toDestroy)
        {
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
    }

    static Material _asphalt;
    static Material _yellow;
    static Material _whitePaint;

    static Material MakeAsphalt()
    {
        if (_asphalt == null)
        {
            _asphalt = new Material(Shader.Find("Standard"));
            _asphalt.color = new Color(0.25f, 0.25f, 0.25f);
            _asphalt.SetFloat("_Glossiness", 0.12f);
            _asphalt.SetFloat("_Metallic", 0f);
            _asphalt.hideFlags = HideFlags.HideAndDontSave;
        }
        return _asphalt;
    }

    void SpawnWideCarriageway(Transform parent, float startX, float length, Material asphalt, Material pavement,
        int skipWalkSide = 0, float skipWalkStart = -1f, float skipWalkEnd = -1f)
    {
        if (asphalt == null) asphalt = MakeAsphalt();
        if (_yellow == null)
        {
            _yellow = new Material(Shader.Find("Standard"));
            _yellow.color = new Color(0.92f, 0.78f, 0.12f);
            _yellow.hideFlags = HideFlags.HideAndDontSave;
        }
        if (_whitePaint == null)
        {
            _whitePaint = new Material(Shader.Find("Standard"));
            _whitePaint.color = new Color(0.92f, 0.92f, 0.90f);
            _whitePaint.hideFlags = HideFlags.HideAndDontSave;
        }

        float y = roadY + 0.02f;
        float half = RoadEdgeZ;
        float cx = startX + length * 0.5f;
        float roadW = half * 2f;

        Slab(parent, asphalt, new Vector3(cx, y, 0f), new Vector3(length, 0.04f, roadW));

        float walk = Mathf.Max(1.6f, sidewalkZ - RoadEdgeZ);
        float walkZ = half + walk * 0.5f;
        var walkMat = pavement != null ? Tiled(pavement, length, walk, 4f, 4f) : asphalt;
        SpawnSidewalk(parent, walkMat, startX, length, y, walkZ, walk, 1, skipWalkSide, skipWalkStart, skipWalkEnd);
        SpawnSidewalk(parent, walkMat, startX, length, y, -walkZ, walk, -1, skipWalkSide, skipWalkStart, skipWalkEnd);

        float lineY = y + 0.022f;
        Slab(parent, _yellow, new Vector3(cx, lineY, half - 0.14f), new Vector3(length, 0.01f, 0.14f));
        Slab(parent, _yellow, new Vector3(cx, lineY, -(half - 0.14f)), new Vector3(length, 0.01f, 0.14f));
        for (float x = 0.4f; x < length; x += 4.5f)
        {
            float dash = Mathf.Min(2.4f, length - x);
            Slab(parent, _whitePaint, new Vector3(startX + x + dash * 0.5f, lineY, 0f), new Vector3(dash, 0.01f, 0.14f));
        }
    }

    void SpawnSidewalk(Transform parent, Material walkMat, float startX, float length, float y,
        float walkZ, float walk, int side, int skipWalkSide, float skipStart, float skipEnd)
    {
        if (skipWalkSide == side && skipEnd > skipStart)
        {
            float a0 = startX;
            float a1 = startX + skipStart;
            float b0 = startX + skipEnd;
            float b1 = startX + length;
            if (a1 - a0 > 0.5f)
                Slab(parent, walkMat, new Vector3((a0 + a1) * 0.5f, y - 0.005f, walkZ), new Vector3(a1 - a0, 0.03f, walk));
            if (b1 - b0 > 0.5f)
                Slab(parent, walkMat, new Vector3((b0 + b1) * 0.5f, y - 0.005f, walkZ), new Vector3(b1 - b0, 0.03f, walk));
            return;
        }
        float cx = startX + length * 0.5f;
        Slab(parent, walkMat, new Vector3(cx, y - 0.005f, walkZ), new Vector3(length, 0.03f, walk));
    }

    static Material Tiled(Material src, float worldX, float worldZ, float tileX, float tileZ)
    {
        var m = new Material(src);
        m.mainTextureScale = new Vector2(worldX / tileX, worldZ / tileZ);
        return m;
    }

    void Slab(Transform parent, Material mat, Vector3 worldPos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Carriageway";
        go.transform.SetParent(parent, false);
        go.transform.position = worldPos;
        go.transform.localScale = size;
        var rend = go.GetComponent<Renderer>();
        if (mat != null) rend.sharedMaterial = mat;
        go.hideFlags = HideFlags.DontSave;
        var col = go.GetComponent<Collider>();
        if (col)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
    }

    void StickWallPosters(List<KeyValuePair<GameObject, int>> placed, Transform chunk, int idx, System.Random rng)
    {
        if (billboardAds == null || placed == null || placed.Count == 0) return;
        int adCount = 0;
        for (int i = 0; i < billboardAds.Length; i++)
            if (billboardAds[i] != null) adCount++;
        if (adCount == 0) return;

        int slot = ((idx % adCount) + adCount) % adCount;
        Texture tex = null;
        int seen = 0;
        for (int i = 0; i < billboardAds.Length; i++)
        {
            if (billboardAds[i] == null) continue;
            if (seen == slot)
            {
                tex = billboardAds[i];
                break;
            }
            seen++;
        }
        if (tex == null) return;

        float xBias = (float)(rng.NextDouble() * 2.0 - 1.0);
        int start = rng.Next(placed.Count);
        for (int a = 0; a < placed.Count && a < 8; a++)
        {
            var pick = placed[(start + a) % placed.Count];
            if (AttachWallPoster(pick.Key, pick.Value, tex, xBias))
                return;
        }
    }

    bool AttachWallPoster(GameObject building, int streetSide, Texture tex, float xBias)
    {
        if (building == null || tex == null) return false;
        if (streetSide == 0) streetSide = building.transform.position.z >= 0f ? 1 : -1;

        var rends = building.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length == 0) return false;
        Bounds wb = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
        {
            if (rends[i] != null && rends[i].enabled)
                wb.Encapsulate(rends[i].bounds);
        }

        if (wb.size.x < 3.5f || wb.size.y < 4.0f) return false;

        // +Z lots face the road in -Z; -Z lots face +Z.
        Vector3 towardStreet = new Vector3(0f, 0f, -streetSide);
        float streetFaceZ = streetSide > 0 ? wb.min.z : wb.max.z;
        float coreZ = wb.center.z;

        // Plants/stairs puff the AABB toward the sidewalk. Pull the poster
        // into the building mass, then sit it just in front of that plane.
        float wallZ = Mathf.Lerp(streetFaceZ, coreZ, 0.62f);
        float minLotZ = streetSide * (sidewalkZ + 3.0f);
        if (streetSide > 0) wallZ = Mathf.Max(wallZ, minLotZ);
        else wallZ = Mathf.Min(wallZ, minLotZ);

        // The placed pivot is on the lot. Don't let a puffed AABB pull the
        // poster out onto the sidewalk in front of the actual wall.
        float pivotZ = building.transform.position.z;
        if (Mathf.Abs(wallZ - pivotZ) > 3.5f)
            wallZ = pivotZ + towardStreet.z * 1.1f;

        float posterH = 2.1f;
        float posterW = posterH * 0.72f;
        posterW = Mathf.Min(posterW, wb.size.x * 0.38f);
        posterH = posterW / 0.72f;

        float x = wb.center.x + xBias * wb.size.x * 0.12f;
        float halfW = posterW * 0.5f + 0.35f;
        x = Mathf.Clamp(x, wb.min.x + halfW, wb.max.x - halfW);

        float y = 2.15f + posterH * 0.5f;
        y = Mathf.Clamp(y, 1.8f, Mathf.Min(4.2f, wb.max.y - posterH * 0.5f - 0.4f));

        Vector3 pos = new Vector3(x, y, wallZ) + towardStreet * 0.06f;

        if (Mathf.Abs(pos.z) < sidewalkZ + 2.4f) return false;
        if (Mathf.Abs(pos.z - coreZ) > wb.extents.z * 0.9f) return false;

        var shader = Shader.Find("IsraeliStreet/BillboardPoster");
        if (shader == null) shader = Shader.Find("Unlit/Texture");
        var mat = new Material(shader);
        mat.hideFlags = HideFlags.DontSave;
        mat.mainTexture = tex;
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "WallPoster_" + tex.name;
        quad.transform.SetParent(building.transform, true);
        quad.transform.position = pos;
        quad.transform.rotation = Quaternion.LookRotation(towardStreet, Vector3.up);
        Vector3 lossy = building.transform.lossyScale;
        float sx = Mathf.Abs(lossy.x) > 0.001f ? posterW / Mathf.Abs(lossy.x) : posterW;
        float sy = Mathf.Abs(lossy.y) > 0.001f ? posterH / Mathf.Abs(lossy.y) : posterH;
        quad.transform.localScale = new Vector3(sx, sy, 1f);

        var col = quad.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        var r = quad.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return true;
    }

    static bool TryGetRendererBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        if (go == null) return false;
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends == null || rends.Length == 0) return false;
        bool any = false;
        for (int i = 0; i < rends.Length; i++)
        {
            if (rends[i] == null || !rends[i].enabled) continue;
            if (!any) { bounds = rends[i].bounds; any = true; }
            else bounds.Encapsulate(rends[i].bounds);
        }
        return any;
    }

    static bool OverlapsGasFootprint(GameObject go, Bounds gasFootprint)
    {
        if (!TryGetRendererBounds(go, out var b)) return false;
        // XZ only — height doesn't matter for lot clearance.
        return b.min.x < gasFootprint.max.x && b.max.x > gasFootprint.min.x
            && b.min.z < gasFootprint.max.z && b.max.z > gasFootprint.min.z;
    }

    void SpawnGasLotPad(Transform parent, Bounds footprint)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "GasLotPad";
        go.transform.SetParent(parent, false);
        float h = 0.08f;
        var c = footprint.center;
        go.transform.position = new Vector3(c.x, h * 0.5f, c.z);
        go.transform.localScale = new Vector3(footprint.size.x + 1.5f, h, footprint.size.z + 1.5f);
        var mat = new Material(Shader.Find("Standard"));
        mat.color = new Color(0.62f, 0.60f, 0.56f);
        mat.SetFloat("_Glossiness", 0.08f);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }
        go.hideFlags = HideFlags.DontSave;
    }

    void NudgeOffGasFootprint(GameObject go, int side, Bounds gasFootprint)
    {
        if (go == null || side == 0) return;
        for (int i = 0; i < 6; i++)
        {
            if (!OverlapsGasFootprint(go, gasFootprint)) return;
            var p = go.transform.position;
            p.z += side * 4f;
            go.transform.position = p;
            GroundAlign(go);
        }
        if (OverlapsGasFootprint(go, gasFootprint))
            DestroySpawned(go);
    }

    void DestroySpawned(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    void GroundAlign(GameObject go)
    {
        var rends = go.GetComponentsInChildren<MeshRenderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float minY = b.min.y;
        float extra = 0f;
        var facing = go.GetComponent<BuildingFacing>();
        if (facing != null) extra = facing.groundOffset;
        var p = go.transform.position;
        go.transform.position = new Vector3(p.x, p.y - minY + extra, p.z);
    }

    void KeepOffRoad(GameObject go, int side)
    {
        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        float roadEdge = sidewalkZ + lotMargin;
        float shift = 0f;
        if (side > 0 && b.min.z < roadEdge) shift = roadEdge - b.min.z;
        else if (side < 0 && b.max.z > -roadEdge) shift = -roadEdge - b.max.z;
        if (Mathf.Abs(shift) > 0.001f)
        {
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y, p.z + shift);
        }
    }

    GameObject InstantiateChild(GameObject prefab, Transform parent)
    {
        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            if (go == null) go = Instantiate(prefab, parent);
        }
        else
#endif
        {
            go = Instantiate(prefab, parent);
        }
        SetHideFlagsRecursive(go.transform);
        return go;
    }

    // Million-vert Meshy facades don't need to receive shadows (casting is enough).
    static void CheapBuilding(GameObject go)
    {
        if (go == null) return;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;
            r.receiveShadows = false;
            var s = r.bounds.size;
            var mat = r.sharedMaterial;
            bool blank = mat == null || mat.mainTexture == null;
            // Sketchfab towers ship with a huge untextured ground plane that reads as a grey box.
            if (blank && s.y < 1.2f && (s.x > 18f || s.z > 18f))
                r.enabled = false;
        }
    }

    static void CheapProp(GameObject go)
    {
        if (go == null) return;
        var rends = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < rends.Length; i++)
        {
            rends[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rends[i].receiveShadows = false;
        }
    }

    void SetHideFlagsRecursive(Transform t)
    {
        t.gameObject.hideFlags = HideFlags.DontSave;
        for (int i = 0; i < t.childCount; i++) SetHideFlagsRecursive(t.GetChild(i));
    }

    BlockType GetBlockType(int chunkIdx)
    {
        BuildBlockSequence();
        int n = Mathf.Max(1, chunksPerBlock);
        int blockFloor = Mathf.FloorToInt((float)chunkIdx / n);
        int blockIdx = Mathf.Clamp(blockFloor, 0, Mathf.Max(1, driveLength) - 1);
        return _blockSequence[blockIdx];
    }

    void BuildBlockSequence()
    {
        int safeCount = Mathf.Max(1, driveLength);
        if (_blockSequence != null && _builtSeqSeed == seedOffset && _builtDriveLength == safeCount) return;
        _builtSeqSeed    = seedOffset;
        _builtDriveLength = safeCount;
        _blockSequence   = new BlockType[safeCount];
        var types = new BlockType[] { BlockType.Playground, BlockType.Parking, BlockType.GasStation };
        var rng = new System.Random(seedOffset ^ 0xBEEF);
        int lastPick = -1;
        for (int i = 0; i < safeCount; i++)
        {
            int pick;
            int tries = 0;
            do { pick = rng.Next(types.Length); tries++; } while (pick == lastPick && tries < 10);
            _blockSequence[i] = types[pick];
            lastPick = pick;
        }
    }
}
