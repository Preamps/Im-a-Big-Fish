using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyMenuUI : MonoBehaviour
{
    private void SaveLocalPlayerName()
    {
        if (GameData.Instance == null)
        {
            return;
        }

        Player[] players = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Player p in players)
        {
            if (p != null && p.IsOwner)
            {
                string networkName = p.playerName.Value.ToString();
                if (!string.IsNullOrEmpty(networkName))
                {
                    GameData.Instance.PlayerName = networkName;
                }
                break;
            }
        }
    }

    public void OnBackToMainMenuClicked()
    {
        SaveLocalPlayerName();

        // Shut down the network connection first
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Load the Menu scene
        SceneManager.LoadScene("Menu");
    }

    public void OnLeaveGameClicked()
    {
        SaveLocalPlayerName();

        // Shut down the network connection first
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Quit the application in a build, stop Play Mode in the editor
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
