using UnityEngine;
using TMPro;
using Unity.Netcode;

public class PlayerMoneyUI : MonoBehaviour
{
    [SerializeField] private TMP_Text moneyText;
    [SerializeField] private string moneyLabelFormat = "Money: {0}";
    [SerializeField] private float findPlayerInterval = 0.25f;

    private Player localPlayer;
    private float nextFindTime;

    void OnEnable()
    {
        TryBindLocalPlayer();
    }

    void OnDisable()
    {
        UnbindPlayer();
    }

    void Update()
    {
        if (localPlayer == null)
        {
            if (Time.time >= nextFindTime)
            {
                nextFindTime = Time.time + Mathf.Max(0.05f, findPlayerInterval);
                TryBindLocalPlayer();
            }
            return;
        }
    }

    private void TryBindLocalPlayer()
    {
        if (localPlayer != null)
        {
            return;
        }

        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player player in players)
        {
            if (player == null || !player.IsSpawned || !player.IsOwner)
            {
                continue;
            }

            BindPlayer(player);
            return;
        }
    }

    private void BindPlayer(Player player)
    {
        localPlayer = player;
        localPlayer.Money.OnValueChanged += OnMoneyChanged;
        RefreshUI(); // Update UI immediately when bound
    }

    private void UnbindPlayer()
    {
        if (localPlayer != null)
        {
            localPlayer.Money.OnValueChanged -= OnMoneyChanged;
        }
        localPlayer = null;
    }

    private void OnMoneyChanged(int prev, int current)
    {
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (moneyText == null || localPlayer == null) return;
        moneyText.text = string.Format(moneyLabelFormat, localPlayer.Money.Value);
    }
}
