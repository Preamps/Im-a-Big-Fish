using UnityEngine;
using TMPro;

public class PlayerInteractionUI : MonoBehaviour
{
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private float findPlayerInterval = 0.25f;

    private Player localPlayer;
    private float nextFindTime;

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

        RefreshUI();
    }

    private void TryBindLocalPlayer()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player player in players)
        {
            if (player == null || !player.IsSpawned || !player.IsOwner)
            {
                continue;
            }

            localPlayer = player;
            return;
        }
    }

    private void RefreshUI()
    {
        if (interactionText == null) return;

        if (localPlayer.NearbyDownedPlayer != null && localPlayer.NearbyDownedPlayer.IsDown.Value)
        {
            interactionText.text = $"Press 'F' to revive {localPlayer.NearbyDownedPlayer.playerName.Value}";
            interactionText.color = Color.green;
        }
        else if (localPlayer.NearbyPickup != null)
        {
            int price = localPlayer.NearbyPickup.price;
            if (localPlayer.Money.Value >= price)
            {
                interactionText.text = $"Press 'E' to buy (-{price} Money)";
                interactionText.color = Color.white;
            }
            else
            {
                interactionText.text = $"Not enough money ({price})";
                interactionText.color = Color.red;
            }
        }
        else if (localPlayer.NearbyBlockade != null)
        {
            int price = localPlayer.NearbyBlockade.price;
            string bName = localPlayer.NearbyBlockade.blockadeName;
            if (localPlayer.Money.Value >= price)
            {
                interactionText.text = $"Press 'E' to clear {bName} (-{price} Money)";
                interactionText.color = Color.white;
            }
            else
            {
                interactionText.text = $"Not enough money ({price})";
                interactionText.color = Color.red;
            }
        }
        else
        {
            // Hide the text when not near anything
            interactionText.text = "";
        }
    }
}
