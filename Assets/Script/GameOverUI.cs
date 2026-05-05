using System.Text;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TMP_Text waveText;
    [SerializeField] private TMP_Text timeText;
    [SerializeField] private TMP_Text playersMoneyText;

    private bool isGameOver = false;
    private bool hasGameStarted = false;
    private float gameStartTime = 0f;

    private void Start()
    {
        if (gameOverPanel != null)
        {
            // If the script is attached directly to the panel we are disabling,
            // the Update method will stop running. So warn the user:
            if (gameOverPanel.gameObject == this.gameObject)
            {
                Debug.LogWarning("GameOverUI is attached to the same GameObject as GameOverPanel! This will stop the script from running. Please attach this script to an empty Manager or Canvas object.");
            }
            gameOverPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (isGameOver) return;

        Player[] players = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        int validPlayersCount = 0;
        bool allDead = true;

        foreach (var p in players)
        {
            if (!p.IsSpawned) continue;

            validPlayersCount++;

            // If a player is fully spawned but neither dead nor down, they are alive
            // We consider the game "over" if everyone is currently down or dead.
            if (!p.IsDead.Value && !p.IsDown.Value)
            {
                allDead = false;
            }
        }

        if (validPlayersCount > 0)
        {
            if (!hasGameStarted && !allDead)
            {
                hasGameStarted = true;
                gameStartTime = Time.time;
            }
            // Trigger if game has started and now everyone is dead
            else if (hasGameStarted && allDead)
            {
                TriggerGameOver();
            }
        }
    }

    private void TriggerGameOver()
    {
        isGameOver = true;
        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        WaveManager waveManager = FindAnyObjectByType<WaveManager>();
        if (waveManager != null)
        {
            if (waveText != null)
                waveText.text = "Last Wave: " + waveManager.CurrentWave.Value;

            // Total time since the game started
            float totalTime = Time.time - gameStartTime;
            int minutes = Mathf.FloorToInt(totalTime / 60F);
            int seconds = Mathf.FloorToInt(totalTime - minutes * 60);

            if (timeText != null)
                timeText.text = string.Format("Total Time: {0:00}:{1:00}", minutes, seconds);
        }

        // Get players money
        Player[] players = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        StringBuilder sb = new StringBuilder();

        // Sort by OwnerClientId to give consistent ordering
        System.Array.Sort(players, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        foreach (var p in players)
        {
            string pName = string.IsNullOrEmpty(p.playerName.Value.ToString())
                           ? $"Player {(p.OwnerClientId + 1)}"
                           : p.playerName.Value.ToString();

            sb.AppendLine($"{pName} Kill {p.KillCount.Value} Money {p.Money.Value}");
        }

        if (playersMoneyText != null)
        {
            playersMoneyText.text = sb.ToString();
        }
    }

    public void OnBackToMainMenuClicked()
    {
        // Shut down the network connection first
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Load the Menu scene
        SceneManager.LoadScene("Menu");
    }
}
