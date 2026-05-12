using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class FadeInOnStart : MonoBehaviour
{
    public Image fadeImage;
    public float fadeDuration = 1.0f;
    public float delayDuration = 3.0f;

    void Awake()
    {
        if (fadeImage != null)
        {
            // This makes the image invisible to mouse clicks/touches immediately
            fadeImage.raycastTarget = false;

            // Ensure it starts fully black
            Color c = fadeImage.color;
            c.a = 1f;
            fadeImage.color = c;
        }
    }

    void Start()
    {
        if (fadeImage != null)
        {
            StartCoroutine(FadeSequence());
        }
    }

    IEnumerator FadeSequence()
    {
        // 1. Wait for 3 seconds (or whatever delayDuration is set to)
        yield return new WaitForSeconds(delayDuration);

        // 2. Fade out the alpha
        float t = 0f;
        Color c = fadeImage.color;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            c.a = Mathf.Lerp(1f, 0f, t / fadeDuration);
            fadeImage.color = c;
            yield return null;
        }

        // 3. Ensure it's fully transparent at the end
        c.a = 0f;
        fadeImage.color = c;

        // Optional: Disable the object to save performance since it's no longer needed
        // gameObject.SetActive(false);
    }
}