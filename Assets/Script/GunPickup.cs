using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class GunPickup : NetworkBehaviour
{
    [Header("Gun Pickup Settings")]
    public int price = 100;
    public GameObject gunPrefab;

    private void OnValidate()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            col.isTrigger = true;
        }
    }
}