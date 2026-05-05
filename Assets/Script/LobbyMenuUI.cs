using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class LobbyMenuUI : MonoBehaviour
{
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

    public void OnQuitGameClicked()
    {
        // Recommended: Shut down network before closing the app 
        // to prevent "ghost" connections on the server/host side
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        Debug.Log("Quit Game button clicked!");
        Application.Quit();
    }
}