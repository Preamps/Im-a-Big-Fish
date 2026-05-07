using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

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
    [SerializeField] private Button[] characterButtons = new Button[4];
    [SerializeField] private Color characterButtonNormalColor = Color.white;
    [SerializeField] private Color characterButtonSelectedColor = Color.green;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;

    [Header("Anti-Spam Settings")]
    [SerializeField] private float buttonCooldown = 1f;

    private float lastHostButtonClickTime = -999f;
    private float lastClientButtonClickTime = -999f;

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

        SetupCharacterButtons();
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

    private void SetupCharacterButtons()
    {
        if (characterButtons == null || characterButtons.Length == 0)
        {
            return;
        }

        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];
            if (button == null)
            {
                continue;
            }

            int buttonIndex = i;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnCharacterSelected(buttonIndex));

            TMP_Text buttonText = button.GetComponentInChildren<TMP_Text>();
            if (buttonText != null)
            {
                buttonText.text = (buttonIndex + 1).ToString();
            }
        }

        int selectedIndex = Mathf.Clamp(GameData.Instance != null ? GameData.Instance.SelectedCharacterIndex : 0, 0, GameData.CharacterCount - 1);
        OnCharacterSelected(selectedIndex);
    }

    private void OnCharacterSelected(int value)
    {
        if (GameData.Instance == null)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(value, 0, GameData.CharacterCount - 1);
        GameData.Instance.SetSelectedCharacterIndex(safeIndex);
        RefreshCharacterButtonVisuals(safeIndex);
    }

    private void RefreshCharacterButtonVisuals(int selectedIndex)
    {
        if (characterButtons == null)
        {
            return;
        }

        for (int i = 0; i < characterButtons.Length; i++)
        {
            Button button = characterButtons[i];
            if (button == null)
            {
                continue;
            }

            ColorBlock colors = button.colors;
            bool isSelected = i == selectedIndex;
            Color targetColor = isSelected ? characterButtonSelectedColor : characterButtonNormalColor;

            colors.normalColor = targetColor;
            colors.selectedColor = targetColor;
            colors.highlightedColor = targetColor;
            colors.pressedColor = isSelected ? Color.Lerp(targetColor, Color.black, 0.15f) : Color.Lerp(targetColor, Color.black, 0.2f);
            button.colors = colors;
        }
    }

    public async void StartHost()
    {
        OnCharacterSelected(GameData.Instance != null ? GameData.Instance.SelectedCharacterIndex : 0);

        // Anti-spam check
        if (Time.time - lastHostButtonClickTime < buttonCooldown)
        {
            return;
        }

        lastHostButtonClickTime = Time.time;

        // Disable button temporarily
        if (hostButton != null) hostButton.interactable = false;

        try
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
        finally
        {
            // Re-enable button after cooldown
            if (hostButton != null) hostButton.interactable = true;
        }
    }

    public async void StartClient()
    {
        OnCharacterSelected(GameData.Instance != null ? GameData.Instance.SelectedCharacterIndex : 0);

        // Anti-spam check
        if (Time.time - lastClientButtonClickTime < buttonCooldown)
        {
            return;
        }

        lastClientButtonClickTime = Time.time;

        // Disable button temporarily
        if (clientButton != null) clientButton.interactable = false;

        try
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
        finally
        {
            // Re-enable button after cooldown
            if (clientButton != null) clientButton.interactable = true;
        }
    }

    public void StartGame()
    {
        HostSingleton.Instance.GameManager.StartGame();
    }
}
