using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WaveCountUI : MonoBehaviour
{
    [SerializeField] private TMP_Text waveText;
    [SerializeField] private string waitingText = "Waiting for wave...";
    [SerializeField] private string runningFormat = "Wave {0}/{1}";
    [SerializeField] private string finishedText = "All waves cleared";
    [SerializeField] private float findWaveManagerInterval = 0.5f;

    private WaveManager waveManager;
    private float nextFindTime;

    void Awake()
    {
        RefreshWaveText();
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
        waveManager = null;
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
        int max = Mathf.Max(1, waveManager.MaxWaves);

        if (!waveManager.IsWaveRunning.Value && current >= max)
        {
            waveText.text = finishedText;
            return;
        }

        if (!waveManager.IsWaveRunning.Value && current <= 0)
        {
            waveText.text = waitingText;
            return;
        }

        waveText.text = string.Format(runningFormat, Mathf.Clamp(current, 1, max), max);
    }
}
