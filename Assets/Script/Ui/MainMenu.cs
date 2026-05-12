using System.Collections;
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
    private Button lobbyJoinCodeButton;
    private string currentJoinCode = string.Empty;
    [SerializeField] private float copyFeedbackDuration = 1.2f;
    private Coroutine copyFeedbackRoutine;
    private bool isShowingCopied = false;

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

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayMusic(SoundType.BackgroundMusic);
        }

        if (menuPanel != null) menuPanel.SetActive(true);
        if (lobbyPanel != null) lobbyPanel.SetActive(false);

        if (playerNameField != null)
        {
            string savedName = GameData.Instance != null ? GameData.Instance.PlayerName : string.Empty;
            if (!string.IsNullOrEmpty(savedName) && savedName != "Player")
            {
                playerNameField.text = savedName;
            }
            else
            {
                playerNameField.text = "Player" + UnityEngine.Random.Range(100, 1000);
            }
        }

        SetupLobbyJoinCodeCopy();

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

        // Ensure host (lowest OwnerClientId) is first, then ascending by OwnerClientId
        System.Array.Sort(allPlayers, (a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));

        for (int i = 0; i < playerNamesTexts.Length; i++)
        {
            if (playerNamesTexts[i] == null) continue;

            if (i < allPlayers.Length && allPlayers[i] != null && allPlayers[i].IsSpawned)
            {
                string displayName = string.IsNullOrEmpty(allPlayers[i].playerName.Value.ToString())
                    ? $"Player {(allPlayers[i].OwnerClientId + 1)}"
                    : allPlayers[i].playerName.Value.ToString();

                playerNamesTexts[i].text = displayName;
                // All occupied lobby slots should be green
                playerNamesTexts[i].color = Color.green;
                playerNamesTexts[i].gameObject.SetActive(true);
            }
            else
            {
                playerNamesTexts[i].text = "Waiting...";
                playerNamesTexts[i].color = Color.white;
                playerNamesTexts[i].gameObject.SetActive(true);
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

    private void SetupLobbyJoinCodeCopy()
    {
        if (lobbyJoinCodeText == null)
        {
            return;
        }

        lobbyJoinCodeButton = lobbyJoinCodeText.GetComponent<Button>();
        if (lobbyJoinCodeButton == null)
        {
            lobbyJoinCodeButton = lobbyJoinCodeText.gameObject.AddComponent<Button>();
        }

        lobbyJoinCodeButton.transition = Selectable.Transition.None;
        lobbyJoinCodeButton.targetGraphic = lobbyJoinCodeText;
        lobbyJoinCodeButton.onClick.RemoveAllListeners();
        lobbyJoinCodeButton.onClick.AddListener(CopyLobbyJoinCode);
    }

    private void SetLobbyJoinCode(string joinCode)
    {
        currentJoinCode = joinCode ?? string.Empty;
        if (lobbyJoinCodeText != null)
        {
            if (!isShowingCopied)
            {
                lobbyJoinCodeText.text = "Code: " + currentJoinCode;
            }
        }
    }

    private void CopyLobbyJoinCode()
    {
        if (string.IsNullOrEmpty(currentJoinCode))
        {
            return;
        }

        GUIUtility.systemCopyBuffer = currentJoinCode;
        ShowCopiedFeedback();
    }

    private void ShowCopiedFeedback()
    {
        if (lobbyJoinCodeText == null)
        {
            return;
        }

        if (copyFeedbackRoutine != null)
        {
            StopCoroutine(copyFeedbackRoutine);
        }

        copyFeedbackRoutine = StartCoroutine(CopyFeedbackRoutine());
    }

    private IEnumerator CopyFeedbackRoutine()
    {
        isShowingCopied = true;
        lobbyJoinCodeText.text = "Copied!";
        yield return new WaitForSeconds(copyFeedbackDuration);
        lobbyJoinCodeText.text = "Code: " + currentJoinCode;
        isShowingCopied = false;
        copyFeedbackRoutine = null;
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
            SetLobbyJoinCode(GameData.Instance.JoinCode);
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
            SetLobbyJoinCode(joinCodeField.text);
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
