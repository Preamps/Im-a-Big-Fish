using UnityEngine;
using Unity.Netcode;

public abstract class Character : NetworkBehaviour
{
    public NetworkVariable<float> Health =
        new NetworkVariable<float>();

    public float Speed { get; protected set; }

    public abstract void Move();
}