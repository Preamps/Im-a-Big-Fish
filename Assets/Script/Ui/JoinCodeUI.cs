using TMPro;
using UnityEngine;

public class JoinCodeUI : MonoBehaviour
{
    public TMP_Text joinCodeText;

    void Start()
    {
        string code = GameData.Instance.JoinCode;

        Debug.Log("Loaded JoinCode: " + code);

        joinCodeText.text = "Code: " + code;
    }
}