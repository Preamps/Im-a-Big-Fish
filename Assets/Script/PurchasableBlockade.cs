using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider2D))]
public class PurchasableBlockade : NetworkBehaviour
{
    [Header("Blockade Settings")]
    public int price = 500;
    public string blockadeName = "Debris";

    private void OnValidate()
    {
        // Blockades usually require a solid collider for physics blocking,
        // and a trigger collider for the Interaction zone.
    }
}
