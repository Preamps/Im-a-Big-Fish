using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInteractionUI : MonoBehaviour
{
    [SerializeField] private TMP_Text interactionText;
    [SerializeField] private Slider reviveHoldSlider;
    [SerializeField] private float findPlayerInterval = 0.25f;
    [SerializeField] private Vector2 sliderOffsetFromText = new Vector2(0, -40f);

    private Player localPlayer;
    private float nextFindTime;
    private RectTransform sliderRectTransform;
    private RectTransform textRectTransform;

    private void Awake()
    {
        if (reviveHoldSlider != null)
        {
            sliderRectTransform = reviveHoldSlider.GetComponent<RectTransform>();
            reviveHoldSlider.minValue = 0f;
            reviveHoldSlider.maxValue = 1f;
            reviveHoldSlider.value = 0f;
            reviveHoldSlider.gameObject.SetActive(false);
        }

        if (interactionText != null)
        {
            textRectTransform = interactionText.GetComponent<RectTransform>();
        }
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
            interactionText.text = $"Hold 'F' to revive {localPlayer.NearbyDownedPlayer.playerName.Value}";
            interactionText.color = Color.green;
            if (reviveHoldSlider != null)
            {
                reviveHoldSlider.gameObject.SetActive(true);
                reviveHoldSlider.value = localPlayer.ReviveHoldProgress;
                PositionSliderNearText();
            }
        }
        else if (localPlayer.NearbyPickup != null)
        {
            SetReviveSliderVisible(false);
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
            SetReviveSliderVisible(false);
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
            SetReviveSliderVisible(false);
        }
    }

    private void SetReviveSliderVisible(bool visible)
    {
        if (reviveHoldSlider == null)
        {
            return;
        }

        reviveHoldSlider.gameObject.SetActive(visible);
        if (!visible)
        {
            reviveHoldSlider.value = 0f;
        }
    }

    private void PositionSliderNearText()
    {
        if (sliderRectTransform == null || textRectTransform == null)
        {
            return;
        }

        sliderRectTransform.position = textRectTransform.position + (Vector3)sliderOffsetFromText;
    }
}
