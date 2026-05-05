using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothTime = 0.2f;
    public Vector3 offset = new Vector3(0, 0, -10);

    [Header("Cursor Dynamics")]
    public float mouseFollowWeight = 0.2f;
    public float maxMouseOffset = 3f;

    private Vector3 velocity = Vector3.zero;
    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPosition = target.position + offset;

        if (cam != null)
        {
            Vector3 mousePos = cam.ScreenToWorldPoint(Input.mousePosition);
            mousePos.z = target.position.z;

            Vector3 mouseOffset = (mousePos - target.position) * mouseFollowWeight;
            if (mouseOffset.magnitude > maxMouseOffset)
            {
                mouseOffset = mouseOffset.normalized * maxMouseOffset;
            }

            desiredPosition += mouseOffset;
        }

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref velocity,
            smoothTime
        );
    }
}