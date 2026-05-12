using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class BloodScreenUI : MonoBehaviour
{
    [SerializeField] private Image bloodOverlay;
    [SerializeField] private float bloodFadeDuration = 0.5f;
    [SerializeField] private float bloodMaxAlpha = 0.6f;

    private Coroutine fadeCoroutine;
    private Player localPlayer;
    private float nextFindTime;
    private float findPlayerInterval = 0.25f;

    private void Awake()
    {
        // Create blood overlay if not assigned
        if (bloodOverlay == null)
        {
            CreateBloodOverlay();
        }

        // Ensure blood overlay starts invisible
        if (bloodOverlay != null)
        {
            Color bloodColor = bloodOverlay.color;
            bloodColor.a = 0f;
            bloodOverlay.color = bloodColor;
        }
    }

    private void CreateBloodOverlay()
    {
        // Create a new GameObject for the blood overlay
        GameObject overlayGO = new GameObject("BloodOverlay");
        overlayGO.transform.SetParent(transform, false);

        // Add Image component
        bloodOverlay = overlayGO.AddComponent<Image>();

        // Set it to fill the entire screen
        RectTransform rectTransform = overlayGO.GetComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        // Set blood red color
        Color bloodColor = new Color(0.8f, 0f, 0f, bloodMaxAlpha);
        bloodOverlay.color = bloodColor;

        // Make sure it's on top of other UI elements
        overlayGO.transform.SetAsLastSibling();
    }

    private void OnEnable()
    {
        TryBindLocalPlayer();
    }

    private void OnDisable()
    {
        UnbindPlayer();
    }

    private void Update()
    {
        if (localPlayer != null) return;
        if (Time.time < nextFindTime) return;

        nextFindTime = Time.time + Mathf.Max(0.05f, findPlayerInterval);
        TryBindLocalPlayer();
    }

    private void TryBindLocalPlayer()
    {
        if (localPlayer != null) return;

        Player[] allPlayers = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (var p in allPlayers)
        {
            if (p.IsOwner)
            {
                BindPlayer(p);
                return;
            }
        }
    }

    private void BindPlayer(Player player)
    {
        localPlayer = player;
    }

    private void UnbindPlayer()
    {
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
            fadeCoroutine = null;
        }
        localPlayer = null;
    }

    public void ShowBloodScreen()
    {
        if (bloodOverlay == null) return;

        // Stop any existing fade coroutine
        if (fadeCoroutine != null)
        {
            StopCoroutine(fadeCoroutine);
        }

        // Show blood at max alpha
        Color bloodColor = bloodOverlay.color;
        bloodColor.a = bloodMaxAlpha;
        bloodOverlay.color = bloodColor;

        // Start fade out
        fadeCoroutine = StartCoroutine(FadeOutBlood());
    }

    private IEnumerator FadeOutBlood()
    {
        float elapsedTime = 0f;
        Color startColor = bloodOverlay.color;
        Color targetColor = startColor;
        targetColor.a = 0f;

        while (elapsedTime < bloodFadeDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.Clamp01(elapsedTime / bloodFadeDuration);

            Color newColor = Color.Lerp(startColor, targetColor, t);
            bloodOverlay.color = newColor;

            yield return null;
        }

        // Ensure it's completely transparent
        Color finalColor = bloodOverlay.color;
        finalColor.a = 0f;
        bloodOverlay.color = finalColor;
        fadeCoroutine = null;
    }
}
