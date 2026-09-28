using UnityEngine;

[ExecuteAlways]
public class StreetCat : MonoBehaviour
{
    public float patrolRange = 5f;
    public float speed       = 0.7f;
    public GameObject catPrefab;

    // Clamp patrol to stay on sidewalk (set by ProceduralStreet)
    public float sidewalkMinZ = 1.5f;
    public float sidewalkMaxZ = 5.0f;

    private Vector3  _origin;
    private float    _dir = 1f;
    private Animator _anim;
    private bool     _built;

    public void Build()
    {
        foreach (Transform c in transform)
        {
            if (Application.isPlaying) Destroy(c.gameObject);
            else DestroyImmediate(c.gameObject);
        }
        if (catPrefab == null) return;

        GameObject go;
#if UNITY_EDITOR
        if (!Application.isPlaying)
            go = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(catPrefab, transform);
        else
#endif
            go = Instantiate(catPrefab, transform);

        if (go == null) return;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.hideFlags = HideFlags.DontSave;

        var rends = go.GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            var b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, p.y - b.min.y, p.z);
        }

        _anim = go.GetComponentInChildren<Animator>();

        _origin = transform.position;
        _built  = true;
    }

    void Start()
    {
        _origin = transform.position;
        _anim   = GetComponentInChildren<Animator>();
        _built  = true;
    }

    void Update()
    {
        if (!Application.isPlaying || !_built) return;

        transform.position += transform.forward * speed * Time.deltaTime;

        float dist = transform.position.x - _origin.x;
        if (dist > patrolRange || dist < -patrolRange)
        {
            _dir = -_dir;
            transform.Rotate(0f, 180f, 0f);
        }

        var p = transform.position;
        int side = p.z >= 0 ? 1 : -1;
        float absZ = Mathf.Abs(p.z);
        absZ = Mathf.Clamp(absZ, sidewalkMinZ, sidewalkMaxZ);
        transform.position = new Vector3(p.x, p.y, side * absZ);

        if (_anim != null) _anim.SetFloat("Vert", 1f);
    }
}
