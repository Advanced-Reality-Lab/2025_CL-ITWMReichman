using UnityEngine;

[DisallowMultipleComponent]
public class BuildingFacing : MonoBehaviour
{
    [Tooltip("Extra yaw (degrees) added to this prefab when placed. Use 90/180/270 to correct prefabs whose native front doesn't point along +Z.")]
    public float yawOffset = 0f;

    [Tooltip("Added to world Y after ground-align. Negative sinks a prefab whose mesh bottom is plants/stairs instead of the building slab.")]
    public float groundOffset = 0f;
}
