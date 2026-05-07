using UnityEngine;

public class SmoothCameraV2 : MonoBehaviour
{
    [Header("Settings")]
    public float maxMoveX = 50f;       // Max horizontal movement in pixels
    public float maxMoveY = 30f;       // Max vertical movement in pixels
    public float smoothSpeed = 5f;

    private Vector2 startPos;
    private RectTransform rect;

    void Awake()
    {
        rect = GetComponent<RectTransform>();
        startPos = rect.anchoredPosition;
    }

    void Update()
    {
        // Normalize mouse position (-1 to 1)
        float mouseX = (Input.mousePosition.x / Screen.width - 0.5f) * 2f;
        float mouseY = (Input.mousePosition.y / Screen.height - 0.5f) * 2f;

        // Calculate target anchored position
        float targetX = Mathf.Clamp(mouseX * maxMoveX, -maxMoveX, maxMoveX);
        float targetY = Mathf.Clamp(mouseY * maxMoveY, -maxMoveY, maxMoveY);
        Vector2 targetPos = startPos + new Vector2(targetX, targetY);

        // Smoothly move
        rect.anchoredPosition = Vector2.Lerp(rect.anchoredPosition, targetPos, Time.deltaTime * smoothSpeed);
    }
}