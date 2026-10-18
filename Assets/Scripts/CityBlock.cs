using System.Collections.Generic;
using UnityEngine;

// A city block is a closed polygon. Buildings are placed along the inside of
// that edge, in one line, with the front facing the street. The middle stays empty.
public class CityBlock : MonoBehaviour
{
    [Tooltip("Boundary corners in local space, on the ground. Walk them in order around the block. The street is outside this shape.")]
    public List<Vector3> boundary = new List<Vector3>();

    [Tooltip("Bauhaus, Arabic, Apartment, and any other building prefabs. One is chosen at random for each slot.")]
    public GameObject[] buildingPrefabs;

    [Tooltip("Smallest gap between two buildings, in meters.")]
    public float minSpacing = 1.5f;

    [Tooltip("Largest gap between two buildings, in meters.")]
    public float maxSpacing = 3f;

    [Tooltip("How far the front face sits inside the boundary, so the building stays off the road.")]
    public float setback = 4f;

    [Tooltip("Empty length left at each corner so buildings do not cross the intersection.")]
    public float cornerMargin = 5f;

    [Tooltip("Street length of every building, in meters. Each prefab is scaled the same on X and Z so the long side matches this. 0 keeps the prefab's own size.")]
    public float frontage = 12f;

    [Tooltip("Extra multiplier on height only. 1 keeps the height that goes with the street length.")]
    public float heightScale = 1f;

    [Tooltip("Same seed places the same buildings again. Change it for a different mix.")]
    public int seed = 1;

    public LayerMask groundMask = ~0;

    [Tooltip("Raycast starts this many meters above each slot.")]
    public float rayStartHeight = 80f;

    public float rayDistance = 200f;

    const string ContainerName = "BlockBuildings";

    public void Generate()
    {
        if (buildingPrefabs == null || buildingPrefabs.Length == 0)
        {
            Debug.LogWarning("CityBlock: assign building prefabs first.", this);
            return;
        }

        var world = WorldBoundary();
        if (world.Count < 3)
        {
            Debug.LogWarning("CityBlock: the block needs at least 3 boundary points.", this);
            return;
        }

        ClearBuildings();
        var container = new GameObject(ContainerName);
        container.transform.SetParent(transform, false);
        RegisterCreated(container);

        float spacingMin = Mathf.Max(0f, Mathf.Min(minSpacing, maxSpacing));
        float spacingMax = Mathf.Max(minSpacing, maxSpacing);
        var rng = new System.Random(seed);
        bool ccw = SignedArea(world) > 0f;
        var placed = new List<Bounds>();
        int spawned = 0;
        int missedGround = 0;

        for (int i = 0; i < world.Count; i++)
        {
            Vector3 a = world[i];
            Vector3 b = world[(i + 1) % world.Count];
            Vector3 edge = b - a;
            edge.y = 0f;
            float length = edge.magnitude;
            if (length < cornerMargin * 2f + 2f) continue;

            Vector3 tangent = edge / length;
            Vector3 outward = Outward(tangent, ccw);
            Vector3 inward = -outward;
            float cursor = cornerMargin;

            while (cursor < length - cornerMargin)
            {
                GameObject prefab = buildingPrefabs[rng.Next(buildingPrefabs.Length)];
                if (prefab == null)
                {
                    cursor += 1f;
                    continue;
                }

                GameObject building = Spawn(prefab, container.transform);
                ApplyScale(building, prefab, this);
                float yawOffset = FacingYaw(building);
                float groundOffset = FacingGround(building);
                float yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg + yawOffset;
                building.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

                float along = SizeAlong(building, tangent);
                float depth = SizeAlong(building, outward);
                if (along < 0.5f) along = 4f;
                if (depth < 0.5f) depth = 4f;

                if (cursor + along > length - cornerMargin)
                {
                    DestroySpawned(building);
                    break;
                }

                float edgeT = (cursor + along * 0.5f) / length;
                Vector3 onEdge = Vector3.Lerp(a, b, edgeT);
                Vector3 center = onEdge + inward * (setback + depth * 0.5f);
                center.y = onEdge.y;
                building.transform.position = center;
                MatchCenter(building, center);

                if (!SnapToGround(building, groundOffset)) missedGround++;

                Bounds footprint = WorldBounds(building);
                if (Overlaps(footprint, placed))
                {
                    DestroySpawned(building);
                    cursor += Mathf.Max(1f, spacingMin);
                    continue;
                }

                placed.Add(footprint);
                spawned++;
                float gap = spacingMin + (float)rng.NextDouble() * (spacingMax - spacingMin);
                cursor += along + gap;
            }
        }

        if (missedGround > 0)
            Debug.LogWarning("CityBlock: " + missedGround + " buildings did not hit a collider. They stayed at the boundary height. The ground needs a collider on a layer included in Ground Mask.", this);
        Debug.Log("CityBlock: placed " + spawned + " buildings along the perimeter.", this);
    }

    public void ClearBuildings()
    {
        var container = transform.Find(ContainerName);
        if (container == null) return;
        DestroySpawned(container.gameObject);
    }

    public void CopyFromPolygonCollider2D()
    {
        var poly = GetComponent<PolygonCollider2D>();
        if (poly == null)
        {
            Debug.LogWarning("CityBlock: no PolygonCollider2D on this object.", this);
            return;
        }

        boundary.Clear();
        Vector2[] pts = poly.points;
        for (int i = 0; i < pts.Length; i++)
            boundary.Add(new Vector3(pts[i].x, 0f, pts[i].y));
    }

    List<Vector3> WorldBoundary()
    {
        var world = new List<Vector3>();
        if (boundary != null && boundary.Count >= 3)
        {
            for (int i = 0; i < boundary.Count; i++)
                world.Add(transform.TransformPoint(boundary[i]));
            return world;
        }

        var poly = GetComponent<PolygonCollider2D>();
        if (poly != null && poly.points != null && poly.points.Length >= 3)
        {
            Vector2[] pts = poly.points;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 local = new Vector3(pts[i].x, 0f, pts[i].y);
                world.Add(poly.transform.TransformPoint(local));
            }
        }
        return world;
    }

    static float SignedArea(List<Vector3> pts)
    {
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 p = pts[i];
            Vector3 q = pts[(i + 1) % pts.Count];
            sum += p.x * q.z - q.x * p.z;
        }
        return sum * 0.5f;
    }

    static Vector3 Outward(Vector3 tangent, bool counterClockwise)
    {
        // Interior is to the left of travel on a counter-clockwise loop, so the street is to the right.
        Vector3 n = counterClockwise
            ? new Vector3(tangent.z, 0f, -tangent.x)
            : new Vector3(-tangent.z, 0f, tangent.x);
        return n.normalized;
    }

    static void ApplyScale(GameObject building, GameObject prefab, CityBlock block)
    {
        Vector3 native = prefab.transform.localScale;
        building.transform.localScale = native;
        building.transform.rotation = Quaternion.identity;
        float longSide = Mathf.Max(SizeAlong(building, Vector3.right), SizeAlong(building, Vector3.forward));
        float xz = 1f;
        if (block.frontage > 0.5f && longSide > 0.05f) xz = block.frontage / longSide;
        float y = Mathf.Max(0.01f, block.heightScale);
        building.transform.localScale = new Vector3(native.x * xz, native.y * xz * y, native.z * xz);
    }

    static float FacingYaw(GameObject building)
    {
        var facing = building.GetComponent<BuildingFacing>();
        return facing != null ? facing.yawOffset : 0f;
    }

    static float FacingGround(GameObject building)
    {
        var facing = building.GetComponent<BuildingFacing>();
        return facing != null ? facing.groundOffset : 0f;
    }

    static float SizeAlong(GameObject building, Vector3 axis)
    {
        axis.y = 0f;
        if (axis.sqrMagnitude < 0.0001f) return 0f;
        axis.Normalize();
        float min = float.MaxValue;
        float max = float.MinValue;
        var renderers = building.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds local = renderers[i].localBounds;
            Vector3 c = local.center;
            Vector3 e = local.extents;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = c + new Vector3(
                    (corner & 1) == 0 ? -e.x : e.x,
                    (corner & 2) == 0 ? -e.y : e.y,
                    (corner & 4) == 0 ? -e.z : e.z);
                Vector3 world = renderers[i].transform.TransformPoint(p);
                float d = world.x * axis.x + world.z * axis.z;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }
        if (min > max) return 0f;
        return max - min;
    }

    static void MatchCenter(GameObject building, Vector3 desired)
    {
        Bounds bounds = WorldBounds(building);
        Vector3 p = building.transform.position;
        p.x += desired.x - bounds.center.x;
        p.z += desired.z - bounds.center.z;
        building.transform.position = p;
    }

    bool SnapToGround(GameObject building, float groundOffset)
    {
        Bounds bounds = WorldBounds(building);
        Vector3 origin = new Vector3(bounds.center.x, bounds.center.y + rayStartHeight, bounds.center.z);
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, rayDistance, groundMask, QueryTriggerInteraction.Ignore);
        float lowest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && hits[i].collider.transform.IsChildOf(building.transform)) continue;
            if (hits[i].point.y < lowest)
            {
                lowest = hits[i].point.y;
                found = true;
            }
        }
        if (!found) return false;

        bounds = WorldBounds(building);
        Vector3 p = building.transform.position;
        p.y += (lowest + groundOffset) - bounds.min.y;
        building.transform.position = p;
        return true;
    }

    static Bounds WorldBounds(GameObject building)
    {
        var renderers = building.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return new Bounds(building.transform.position, Vector3.one);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static bool Overlaps(Bounds candidate, List<Bounds> placed)
    {
        candidate.Expand(new Vector3(-0.05f, 0f, -0.05f));
        for (int i = 0; i < placed.Count; i++)
        {
            if (candidate.Intersects(placed[i])) return true;
        }
        return false;
    }

    static GameObject Spawn(GameObject prefab, Transform parent)
    {
#if UNITY_EDITOR
        if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
        {
            var spawned = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, parent);
            RegisterCreated(spawned);
            return spawned;
        }
#endif
        var copy = Instantiate(prefab, parent);
        copy.name = prefab.name;
        RegisterCreated(copy);
        return copy;
    }

    static void DestroySpawned(GameObject go)
    {
        if (go == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.DestroyObjectImmediate(go);
            return;
        }
#endif
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }

    static void RegisterCreated(GameObject go)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && go != null)
            UnityEditor.Undo.RegisterCreatedObjectUndo(go, "Generate City Block");
#endif
    }
}
