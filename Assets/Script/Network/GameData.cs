using UnityEngine;

public class GameData : MonoBehaviour
{
    public static GameData Instance;

    public string JoinCode;
    public string PlayerName = "Player";
    public const int CharacterCount = 4;
    public int SelectedCharacterIndex;

    public void SetSelectedCharacterIndex(int index)
    {
        SelectedCharacterIndex = Mathf.Clamp(index, 0, CharacterCount - 1);
    }

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SetSelectedCharacterIndex(0);
    }

    // ✅ ตัวนี้สำคัญมาก
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init()
    {
        if (Instance == null)
        {
            GameObject obj = new GameObject("GameData");
            obj.AddComponent<GameData>();
        }
    }
}