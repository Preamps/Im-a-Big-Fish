using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WaveCountUI : MonoBehaviour
{
    [SerializeField] private TMP_Text waveText;
    [SerializeField] private string waitingText = "Waiting for wave...";
    [SerializeField] private string runningFormat = "Wave {0}";
    [SerializeField] private string finishedText = "All waves cleared";
    [SerializeField] private float findWaveManagerInterval = 0.5f;
    [SerializeField] private float waveClearDisplayTime = 15f;
    [SerializeField] private TMP_Text waveClearText;

    private WaveManager waveManager;
    private float nextFindTime;
    private Coroutine waveClearCoroutine;

    void Awake()
    {
        RefreshWaveText();
        if (waveClearText != null)
        {
            waveClearText.gameObject.SetActive(false);
        }
    }

    void OnEnable()
    {
        TryBindWaveManager();
    }

    void OnDisable()
    {
        UnbindWaveManager();
    }

    void Update()
    {
        if (waveManager != null)
        {
            return;
        }

        if (Time.time < nextFindTime)
        {
            return;
        }

        nextFindTime = Time.time + Mathf.Max(0.1f, findWaveManagerInterval);
        TryBindWaveManager();
    }

    private void TryBindWaveManager()
    {
        if (waveManager != null)
        {
            return;
        }

        WaveManager[] managers = FindObjectsByType<WaveManager>(FindObjectsSortMode.None);
        if (managers == null || managers.Length == 0)
        {
            RefreshWaveText();
            return;
        }

        waveManager = managers[0];
        waveManager.CurrentWave.OnValueChanged += OnWaveValueChanged;
        waveManager.IsWaveRunning.OnValueChanged += OnWaveStateChanged;
        // subscribe to wave cleared signal
        waveManager.WaveClearedSignal.OnValueChanged += OnWaveCleared;

        RefreshWaveText();
    }

    private void UnbindWaveManager()
    {
        if (waveManager == null)
        {
            return;
        }

        waveManager.CurrentWave.OnValueChanged -= OnWaveValueChanged;
        waveManager.IsWaveRunning.OnValueChanged -= OnWaveStateChanged;
        waveManager.WaveClearedSignal.OnValueChanged -= OnWaveCleared;
        waveManager = null;
    }

    private void OnWaveCleared(int previousValue, int newValue)
    {
        if (waveClearText != null)
        {
            if (waveClearCoroutine != null)
            {
                StopCoroutine(waveClearCoroutine);
                waveClearCoroutine = null;
            }

            waveClearText.text = "Wave Clear!!!";
            waveClearText.gameObject.SetActive(true);
            waveClearCoroutine = StartCoroutine(ShowWaveClearedRoutine());
            return;
        }

        // fallback to using the main waveText if no separate text provided
        if (waveText == null) return;

        if (waveClearCoroutine != null)
        {
            StopCoroutine(waveClearCoroutine);
            waveClearCoroutine = null;
        }

        waveClearCoroutine = StartCoroutine(ShowWaveClearedRoutine());
    }

    private IEnumerator ShowWaveClearedRoutine()
    {
        if (waveClearText != null)
        {
            yield return new WaitForSeconds(Mathf.Max(0.01f, waveClearDisplayTime));
            waveClearText.gameObject.SetActive(false);
            waveClearCoroutine = null;
            yield break;
        }

        string prev = waveText.text;
        waveText.text = "Wave Clear!!!";
        yield return new WaitForSeconds(Mathf.Max(0.01f, waveClearDisplayTime));
        RefreshWaveText();
        waveClearCoroutine = null;
    }

    private void OnWaveValueChanged(int previousValue, int newValue)
    {
        RefreshWaveText();
    }

    private void OnWaveStateChanged(bool previousValue, bool newValue)
    {
        RefreshWaveText();
    }

    private void RefreshWaveText()
    {
        if (waveText == null)
        {
            return;
        }

        if (waveManager == null)
        {
            waveText.text = waitingText;
            return;
        }

        int current = Mathf.Max(0, waveManager.CurrentWave.Value);

        if (!waveManager.IsWaveRunning.Value && current <= 0)
        {
            waveText.text = waitingText;
            return;
        }

        try
        {
            waveText.text = string.Format(runningFormat, current, "∞");
        }
        catch
        {
            waveText.text = "Wave " + current;
        }
    }
}
