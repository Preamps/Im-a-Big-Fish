using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField] private Slider healthSlider;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private string healthLabelFormat = "HP {0:0}/{1:0}";
    [SerializeField] private float fallbackMaxHealth = 100f;
    [SerializeField] private float findPlayerInterval = 0.25f;

    private Player localPlayer;
    private float nextFindTime;

    void Awake()
    {
        if (healthSlider != null)
        {
            healthSlider.minValue = 0f;
            healthSlider.maxValue = Mathf.Max(1f, fallbackMaxHealth);
            healthSlider.value = healthSlider.maxValue;
        }

        UpdateLabel(Mathf.Max(1f, fallbackMaxHealth), Mathf.Max(1f, fallbackMaxHealth));
    }

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
        if (localPlayer != null) return;
        if (Time.time < nextFindTime) return;

        nextFindTime = Time.time + Mathf.Max(0.05f, findPlayerInterval);
        TryBindLocalPlayer();
    }

    private void TryBindLocalPlayer()
    {
        if (localPlayer != null)
        {
            return;
        }

        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
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
        localPlayer.Health.OnValueChanged += OnHealthChanged;

        float maxHp = Mathf.Max(1f, localPlayer.MaxHealth);
        if (healthSlider != null)
        {
            healthSlider.maxValue = maxHp;
        }

        RefreshUI(localPlayer.Health.Value, maxHp);
    }

    private void UnbindPlayer()
    {
        if (localPlayer != null)
        {
            localPlayer.Health.OnValueChanged -= OnHealthChanged;
            localPlayer = null;
        }
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (localPlayer == null)
        {
            return;
        }

        RefreshUI(newValue, Mathf.Max(1f, localPlayer.MaxHealth));
    }

    private void RefreshUI(float currentHp, float maxHp)
    {
        float safeMax = Mathf.Max(1f, maxHp);
        float safeCurrent = Mathf.Clamp(currentHp, 0f, safeMax);

        if (healthSlider != null)
        {
            healthSlider.maxValue = safeMax;
            healthSlider.value = safeCurrent;
        }

        UpdateLabel(safeCurrent, safeMax);
    }

    private void UpdateLabel(float currentHp, float maxHp)
    {
        if (healthText == null)
        {
            return;
        }

        healthText.text = string.Format(healthLabelFormat, currentHp, maxHp);
    }
}
