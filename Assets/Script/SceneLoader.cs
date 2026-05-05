using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement; // Required for switching scenes

public class SceneLoader : MonoBehaviour
{
    // Call this function from your UI Button
    public void LoadSceneByName(string loading)
    {
        SceneManager.LoadScene(loading);
    }
}