using UnityEngine;

public class CrosshairController : MonoBehaviour
{
    public RectTransform crosshair;

    void Start()
    {
        // ซ่อน Cursor และจำกัดขอบเขต
        SetCursorState(false);
    }

    void Update()
    {
        if (crosshair == null) return;

        // ให้ UI วิ่งตาม Mouse
        crosshair.position = Input.mousePosition;
    }

    // ฟังก์ชันช่วยจัดการสถานะ Cursor ให้เป็นระเบียบ
    private void SetCursorState(bool isVisible)
    {
        Cursor.visible = isVisible;
        Cursor.lockState = isVisible ? CursorLockMode.None : CursorLockMode.Confined;
    }

    // 🔥 ทำงานอัตโนมัติเมื่อ Scene เปลี่ยน หรือ Object นี้ถูกทำลาย
    private void OnDisable()
    {
        SetCursorState(true);
    }

    public void SetActive(bool isActive)
    {
        if (crosshair != null)
            crosshair.gameObject.SetActive(isActive);
    }
}