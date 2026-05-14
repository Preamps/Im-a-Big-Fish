using UnityEngine;
using UnityEngine.UI;

public class CrosshairController : MonoBehaviour
{
    public RectTransform crosshair;

    [Header("Reload UI")]
    [SerializeField] private Image reloadCircle;

    [Header("Crosshair Expand")]
    [SerializeField] private float expandStep = 0.15f;
    [SerializeField] private float maxExpand = 0.6f;
    [SerializeField] private float expandSpeed = 18f;
    [SerializeField] private float shrinkSpeed = 10f;

    private Vector3 baseScale = Vector3.one;
    private float currentExpand;
    private float targetExpand;
    private bool hasBaseScale;
    private bool isReloading;
    private float reloadStartTime;
    private float reloadDuration = 1f;

    void OnEnable()
    {
        Gun.OnLocalShot += HandleLocalShot;
        Gun.OnLocalReloadStart += HandleLocalReloadStart;
        Gun.OnLocalReloadEnd += HandleLocalReloadEnd;
        CacheBaseScale();
    }

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
        if (reloadCircle != null)
        {
            reloadCircle.rectTransform.position = Input.mousePosition;
        }

        if (isReloading && crosshair.gameObject.activeInHierarchy)
        {
            CancelReloadUi();
        }

        UpdateExpand();
        UpdateReloadFill();
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
        Gun.OnLocalShot -= HandleLocalShot;
        Gun.OnLocalReloadStart -= HandleLocalReloadStart;
        Gun.OnLocalReloadEnd -= HandleLocalReloadEnd;
        SetCursorState(true);
    }

    public void SetActive(bool isActive)
    {
        if (crosshair != null)
            crosshair.gameObject.SetActive(isActive);

        if (!isActive)
        {
            CancelReloadUi();
            return;
        }

        if (isReloading)
        {
            CancelReloadUi();
        }
    }

    public void ForceShowCrosshair()
    {
        CancelReloadUi();
        if (crosshair != null)
        {
            crosshair.gameObject.SetActive(true);
        }
    }

    private void HandleLocalShot()
    {
        targetExpand = Mathf.Min(targetExpand + expandStep, maxExpand);
    }

    private void UpdateExpand()
    {
        CacheBaseScale();
        if (!hasBaseScale) return;
        if (isReloading) return;

        targetExpand = Mathf.MoveTowards(targetExpand, 0f, Time.deltaTime * shrinkSpeed);
        float speed = targetExpand > currentExpand ? expandSpeed : shrinkSpeed;
        currentExpand = Mathf.MoveTowards(currentExpand, targetExpand, Time.deltaTime * speed);

        crosshair.localScale = baseScale * (1f + currentExpand);
    }

    private void CacheBaseScale()
    {
        if (crosshair == null || hasBaseScale) return;

        baseScale = crosshair.localScale;
        hasBaseScale = true;
    }

    private void HandleLocalReloadStart(float duration)
    {
        isReloading = true;
        reloadStartTime = Time.time;
        reloadDuration = Mathf.Max(0.01f, duration);
        if (crosshair != null)
        {
            crosshair.gameObject.SetActive(false);
        }

        if (reloadCircle != null)
        {
            reloadCircle.gameObject.SetActive(true);
            reloadCircle.fillAmount = 0f;
        }
    }

    private void HandleLocalReloadEnd()
    {
        isReloading = false;
        if (crosshair != null)
        {
            crosshair.gameObject.SetActive(true);
        }

        if (reloadCircle != null)
        {
            reloadCircle.fillAmount = 0f;
            reloadCircle.gameObject.SetActive(false);
        }
    }

    private void CancelReloadUi()
    {
        isReloading = false;
        if (reloadCircle != null)
        {
            reloadCircle.fillAmount = 0f;
            reloadCircle.gameObject.SetActive(false);
        }
    }

    private void UpdateReloadFill()
    {
        if (!isReloading || reloadCircle == null) return;

        float elapsed = Time.time - reloadStartTime;
        float t = Mathf.Clamp01(elapsed / reloadDuration);
        reloadCircle.fillAmount = t;
    }
}