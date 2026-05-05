using UnityEngine;
using TMPro;

public class EnemyCountUI : MonoBehaviour
{
    [SerializeField] private TMP_Text enemyCountText;
    [SerializeField] private string waitingText = "Enemies: 0";
    [SerializeField] private string runningFormat = "Enemies: {0}";
    [SerializeField] private float findWaveManagerInterval = 0.5f;

    private WaveManager waveManager;
    private float nextFindTime;

    void Awake()
    {
        RefreshEnemyText();
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
            RefreshEnemyText();
            return;
        }

        waveManager = managers[0];
        waveManager.EnemiesRemaining.OnValueChanged += OnEnemyCountChanged;
        waveManager.IsWaveRunning.OnValueChanged += OnWaveStateChanged;

        RefreshEnemyText();
    }

    private void UnbindWaveManager()
    {
        if (waveManager == null)
        {
            return;
        }

        waveManager.EnemiesRemaining.OnValueChanged -= OnEnemyCountChanged;
        waveManager.IsWaveRunning.OnValueChanged -= OnWaveStateChanged;
        waveManager = null;
    }

    private void OnEnemyCountChanged(int previousValue, int newValue)
    {
        RefreshEnemyText();
    }

    private void OnWaveStateChanged(bool previousValue, bool newValue)
    {
        RefreshEnemyText();
    }

    private void RefreshEnemyText()
    {
        if (enemyCountText == null)
        {
            return;
        }

        if (waveManager == null || !waveManager.IsWaveRunning.Value)
        {
            enemyCountText.text = waitingText;
            return;
        }

        enemyCountText.text = string.Format(runningFormat, waveManager.EnemiesRemaining.Value);
    }
}