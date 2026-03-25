using UnityEngine;
using Unity.Netcode;

public abstract class Character : NetworkBehaviour
{
    public NetworkVariable<float> Health =
        new NetworkVariable<float>(100f);

    public float Speed { get; protected set; }

    public abstract void Move();

    public virtual void TakeDamage(float dmg)
    {
        if (!IsServer) return;

        Health.Value -= dmg;

        if (Health.Value <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log($"{OwnerClientId} died");

        // ตัวอย่างง่ายๆ
        GetComponent<NetworkObject>().Despawn();
    }
}