using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using TMPro;

public class PlayerAmmoUI : MonoBehaviour
{
    [SerializeField] private TMP_Text ammoText;
    [SerializeField] private string ammoLabelFormat = "Ammo: {0}/{1}";
    [SerializeField] private string reloadingLabel = "Reloading...";
    [SerializeField] private float findPlayerInterval = 0.25f;

    private Player localPlayer;
    private Gun localGun;
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
        if (localPlayer == null || localGun == null)
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
        if (localPlayer != null && localGun != null)
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
        localGun = player.GetComponentInChildren<Gun>(true);
    }

    private void UnbindPlayer()
    {
        localPlayer = null;
        localGun = null;
    }

    private void RefreshUI()
    {
        if (ammoText == null) return;

        if (localGun.IsReloading)
        {
            ammoText.text = reloadingLabel;
        }
        else
        {
            ammoText.text = string.Format(ammoLabelFormat, localGun.CurrentAmmo, localGun.maxAmmo);
        }
    }
}