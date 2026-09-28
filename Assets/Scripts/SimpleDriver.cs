using UnityEngine;

[RequireComponent(typeof(Transform))]
public class SimpleDriver : MonoBehaviour
{
    public float forwardSpeed = 12f;
    public bool autoDrive = true;
    public float turnRate = 40f;

    [Tooltip("Auto-drive for this many seconds then loop back. 0 = infinite.")]
    public float driveDurationSeconds = 300f; // 5 minutes

    [Tooltip("X position to teleport back to when the loop resets.")]
    public float loopStartX = 0f;

    [Tooltip("World Z of the driving lane. Negative Z is the right lane when facing +X.")]
    public float laneZ = -ProceduralStreet.RoadEdgeZ * 0.5f;

    [Tooltip("Keep auto-drive in the lane (ignore stray Z drift).")]
    public bool lockToLane = true;

    private CharacterController _cc;
    private float _elapsed;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
    }

    void Start()
    {
        SnapToLane();
    }

    void Update()
    {
        float dt = Time.deltaTime;
        float speed = autoDrive ? forwardSpeed : 0f;

        if (!autoDrive)
        {
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) speed = forwardSpeed;
            else if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) speed = -forwardSpeed * 0.5f;
        }

        float turn = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) turn -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) turn += 1f;
        if (turn != 0f) transform.Rotate(0f, turn * turnRate * dt, 0f);

        Vector3 delta = transform.forward * speed * dt;
        if (_cc != null && _cc.enabled)
            _cc.Move(delta);
        else
            transform.position += delta;

        if (autoDrive && lockToLane)
            SnapToLane(keepX: true);

        // Loop back after driveDurationSeconds without teleporting — just reset position
        if (autoDrive && driveDurationSeconds > 0f)
        {
            _elapsed += dt;
            if (_elapsed >= driveDurationSeconds)
            {
                _elapsed = 0f;
                var p = transform.position;
                SetPosition(new Vector3(loopStartX, p.y, lockToLane ? laneZ : p.z));
            }
        }
    }

    void SnapToLane(bool keepX = true)
    {
        var p = transform.position;
        float x = keepX ? p.x : loopStartX;
        if (Mathf.Abs(p.z - laneZ) < 0.001f && Mathf.Abs(p.x - x) < 0.001f)
            return;
        SetPosition(new Vector3(x, p.y, laneZ));
    }

    void SetPosition(Vector3 pos)
    {
        bool ccOn = _cc != null && _cc.enabled;
        if (ccOn) _cc.enabled = false;
        transform.position = pos;
        if (ccOn) _cc.enabled = true;
    }
}
