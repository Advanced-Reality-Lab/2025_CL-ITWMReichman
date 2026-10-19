using UnityEngine;

// Walks a closed loop of sidewalk points (world space) in Play mode, playing the model's walk clip.
public class CityWalker : MonoBehaviour
{
    public Vector3[] path;
    public float speed = 1.3f;
    public float distance;          // current distance along the loop
    public bool reverse;

    float[] _cum;
    float _total;

    void Start()
    {
        Prepare();
        foreach (var anim in GetComponentsInChildren<Animation>())
        {
            anim.wrapMode = WrapMode.Loop;
            if (anim.clip != null) anim[anim.clip.name].speed = speed / 1.3f;
            anim.Play();
        }
    }

    void Prepare()
    {
        if (path == null || path.Length < 2) return;
        _cum = new float[path.Length + 1];
        for (int i = 0; i < path.Length; i++)
            _cum[i + 1] = _cum[i] + Vector3.Distance(path[i], path[(i + 1) % path.Length]);
        _total = _cum[path.Length];
    }

    void Update()
    {
        if (_cum == null || _total < 0.1f) return;
        distance = Mathf.Repeat(distance + (reverse ? -speed : speed) * Time.deltaTime, _total);
        Vector3 dir;
        transform.position = Sample(distance, out dir);
        if (reverse) dir = -dir;
        if (dir.sqrMagnitude > 1e-4f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), 6f * Time.deltaTime);
    }

    public Vector3 Sample(float s, out Vector3 dir)
    {
        if (_cum == null) Prepare();
        int n = path.Length;
        for (int i = 0; i < n; i++)
        {
            if (s > _cum[i + 1]) continue;
            Vector3 a = path[i], b = path[(i + 1) % n];
            float seg = _cum[i + 1] - _cum[i];
            dir = (b - a).normalized;
            return Vector3.Lerp(a, b, seg < 1e-4f ? 0f : (s - _cum[i]) / seg);
        }
        dir = (path[1] - path[0]).normalized;
        return path[0];
    }
}
