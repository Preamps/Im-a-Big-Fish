using UnityEngine;
using TMPro;
using Unity.Collections;
using Unity.Netcode;

public class PlayerNameDisplay : MonoBehaviour
{
    [SerializeField] private Player player;
    [SerializeField] private TMP_Text playerNameText;

    private void Start()
    {
        if (player == null) player = GetComponentInParent<Player>();

        // สมัครรับ Event เมื่อชื่อถูกเปลี่ยน (จากค่าเริ่มต้น เป็นชื่อจาก Lobby)
        player.PlayerName.OnValueChanged += HandlePlayerNameChanged;

        // แสดงชื่อที่มีอยู่ตอนนี้ก่อน
        UpdateDisplayName(player.PlayerName.Value);
    }

    private void HandlePlayerNameChanged(FixedString32Bytes oldName, FixedString32Bytes newName)
    {
        UpdateDisplayName(newName);
    }

    private void UpdateDisplayName(FixedString32Bytes name)
    {
        if (playerNameText != null)
        {
            playerNameText.text = name.ToString();
        }
    }

    private void OnDestroy()
    {
        if (player != null)
            player.PlayerName.OnValueChanged -= HandlePlayerNameChanged;
    }
}