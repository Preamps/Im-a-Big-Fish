using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class AmmoPickup : NetworkBehaviour
{
    [Header("Ammo Pickup Settings")]
    public int ammoAmount = 15;

    private void OnValidate()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }

        ammoAmount = Mathf.Max(1, ammoAmount);
    }
}