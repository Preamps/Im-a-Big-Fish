using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    public Transform cameraTransform;
    public float parallaxMultiplier = 0.5f;

    private Vector3 lastCameraPosition;

    void Start()
    {
        if (cameraTransform == null)
            cameraTransform = Camera.main.transform;

        lastCameraPosition = cameraTransform.position;
    }

    void LateUpdate()
    {
        Vector3 deltaMovement =
            cameraTransform.position - lastCameraPosition;

        transform.position += new Vector3(
            deltaMovement.x * parallaxMultiplier,
            deltaMovement.y * parallaxMultiplier,
            0
        );

        lastCameraPosition = cameraTransform.position;
    }
}