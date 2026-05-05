using TMPro;
using UnityEngine;

public class MainMenu : MonoBehaviour
{
    [Header("Menu Panels")]
    [SerializeField] private GameObject menuPanel;
    [SerializeField] private GameObject lobbyPanel;

    [Header("UI References")]
    [SerializeField] private TMP_InputField playerNameField;
    [SerializeField] private TMP_InputField joinCodeField;
    [SerializeField] private TMP_Text lobbyJoinCodeText;
    [SerializeField] private GameObject startGameButton;
    [SerializeField] private TMP_Text[] playerNamesTexts = new TMP_Text[4];

    private void Start()
    {
        if (ClientSingleton.Instance == null)
        {
            return;
        }
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);

        if (menuPanel != null) menuPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        if (playerNameField != null)
        {
            playerNameField.text = "Player" + UnityEngine.Random.Range(100, 1000);
        }
    }

    private void Update()
    {
        if (lobbyPanel != null && lobbyPanel.activeInHierarchy)
        {
            UpdateLobbyPlayers();
        }
    }

    private void UpdateLobbyPlayers()
    {
        if (playerNamesTexts == null || playerNamesTexts.Length == 0) return;

        Player[] allPlayers = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);

        for (int i = 0; i < playerNamesTexts.Length; i++)
        {
            if (playerNamesTexts[i] == null) continue;

            if (i < allPlayers.Length)
            {
                playerNamesTexts[i].text = allPlayers[i].playerName.Value.ToString();
                playerNamesTexts[i].gameObject.SetActive(true);
            }
            else
            {
                playerNamesTexts[i].text = "Waiting...";
                // Keep it active but show "Waiting..." or you can disable it
            }
        }
    }

    public async void StartHost()
    {
        if (playerNameField != null && !string.IsNullOrEmpty(playerNameField.text))
        {
            GameData.Instance.PlayerName = playerNameField.text;
        }

        await HostSingleton.Instance.GameManager.StartHostAsync();

        // Show lobby panel
        if (menuPanel != null) menuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        // Show code and start button for host
        if (lobbyJoinCodeText != null) lobbyJoinCodeText.text = "Code: " + GameData.Instance.JoinCode;
        if (startGameButton != null) startGameButton.SetActive(true);
    }

    public async void StartClient()
    {
        if (playerNameField != null && !string.IsNullOrEmpty(playerNameField.text))
        {
            GameData.Instance.PlayerName = playerNameField.text;
        }

        await ClientSingleton.Instance.GameManager.StartClientAsync(joinCodeField.text);

        // Show lobby panel for client
        if (menuPanel != null) menuPanel.SetActive(false);
        if (lobbyPanel != null) lobbyPanel.SetActive(true);

        // Hide start button for client (only host can start)
        if (lobbyJoinCodeText != null) lobbyJoinCodeText.text = "Code: " + joinCodeField.text;
        if (startGameButton != null) startGameButton.SetActive(false);
    }

    public void StartGame()
    {
        HostSingleton.Instance.GameManager.StartGame();
    }
}
