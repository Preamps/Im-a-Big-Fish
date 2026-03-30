using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    [Header("References")]
    public Transform cameraTransform;

    [Header("Movement")]
    [Range(-2f, 2f)] public float parallaxMultiplier = 0.5f;
    public bool affectX = true;
    public bool affectY = false;

    [Header("Depth Based Multiplier")]
    public bool useDepthMultiplier;
    public float depthReference = 10f;

    [Header("Startup")]
    public bool autoFindMainCamera = true;
    public bool recaptureOnEnable = true;

    private Vector3 startLayerPosition;
    private Vector3 startCameraPosition;
    private bool initialized;

    void Awake()
    {
        TryInitialize();
    }

    void OnEnable()
    {
        if (recaptureOnEnable)
        {
            CaptureStartPositions();
        }
    }

    void LateUpdate()
    {
        if (!TryInitialize()) return;

        Vector3 cameraDelta = cameraTransform.position - startCameraPosition;
        float multiplier = GetEffectiveMultiplier();

        float x = affectX ? startLayerPosition.x + (cameraDelta.x * multiplier) : startLayerPosition.x;
        float y = affectY ? startLayerPosition.y + (cameraDelta.y * multiplier) : startLayerPosition.y;

        transform.position = new Vector3(x, y, startLayerPosition.z);
    }

    float GetEffectiveMultiplier()
    {
        if (!useDepthMultiplier || cameraTransform == null)
            return parallaxMultiplier;

        float safeReference = Mathf.Max(0.001f, Mathf.Abs(depthReference));
        float distance = Mathf.Abs(startLayerPosition.z - cameraTransform.position.z);

        return parallaxMultiplier * (distance / safeReference);
    }

    bool TryInitialize()
    {
        if (initialized && cameraTransform != null)
            return true;

        if (cameraTransform == null && autoFindMainCamera)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                cameraTransform = mainCamera.transform;
            }
        }

        if (cameraTransform == null)
            return false;

        CaptureStartPositions();
        initialized = true;
        return true;
    }

    public void CaptureStartPositions()
    {
        if (cameraTransform == null)
            return;

        startLayerPosition = transform.position;
        startCameraPosition = cameraTransform.position;
    }
}