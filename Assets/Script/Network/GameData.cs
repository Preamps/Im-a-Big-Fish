using UnityEngine;

public class GameData : MonoBehaviour
{
    public static GameData Instance;

    public string JoinCode;
    public string PlayerName = "Player";

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
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