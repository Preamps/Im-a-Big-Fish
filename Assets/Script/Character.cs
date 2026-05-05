using UnityEngine;
using Unity.Netcode;
using System.Collections;

public abstract class Character : NetworkBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private bool despawnOnDeath = true;
    [SerializeField] private Color damageFlashColor = new Color(1f, 0.3f, 0.3f, 1f);
    [SerializeField] private float damageFlashDuration = 0.1f;
    [SerializeField] private SpriteRenderer[] damageFlashRenderers;

    public NetworkVariable<float> Health =
        new NetworkVariable<float>(100f);
    public NetworkVariable<bool> IsDead =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public float Speed { get; protected set; }
    public float MaxHealth => maxHealth;
    private Color[] baseSpriteColors;
    private Coroutine damageFlashRoutine;

    public override void OnNetworkSpawn()
    {
        ResolveDamageFlashRenderers();
        Health.OnValueChanged += OnHealthChanged;

        if (!IsServer) return;

        Health.Value = Mathf.Max(1f, maxHealth);
        IsDead.Value = false;
    }

    public override void OnNetworkDespawn()
    {
        Health.OnValueChanged -= OnHealthChanged;
        ResetDamageFlash();
    }

    public abstract void Move();

    private void ResolveDamageFlashRenderers()
    {
        if (damageFlashRenderers == null || damageFlashRenderers.Length == 0)
        {
            damageFlashRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        }

        baseSpriteColors = new Color[damageFlashRenderers.Length];
        for (int i = 0; i < damageFlashRenderers.Length; i++)
        {
            SpriteRenderer sr = damageFlashRenderers[i];
            baseSpriteColors[i] = sr != null ? sr.color : Color.white;
        }
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (newValue < previousValue)
        {
            PlayDamageFlash();
        }
    }

    private void PlayDamageFlash()
    {
        if (damageFlashRenderers == null || damageFlashRenderers.Length == 0)
        {
            ResolveDamageFlashRenderers();
        }

        if (damageFlashRenderers == null || damageFlashRenderers.Length == 0)
        {
            return;
        }

        if (damageFlashRoutine != null)
        {
            StopCoroutine(damageFlashRoutine);
        }

        damageFlashRoutine = StartCoroutine(DamageFlashRoutine());
    }

    private IEnumerator DamageFlashRoutine()
    {
        for (int i = 0; i < damageFlashRenderers.Length; i++)
        {
            SpriteRenderer sr = damageFlashRenderers[i];
            if (sr != null)
            {
                sr.color = damageFlashColor;
            }
        }

        yield return new WaitForSeconds(Mathf.Max(0.02f, damageFlashDuration));

        ResetDamageFlash();
    }

    private void ResetDamageFlash()
    {
        if (damageFlashRoutine != null)
        {
            StopCoroutine(damageFlashRoutine);
            damageFlashRoutine = null;
        }

        if (damageFlashRenderers == null || baseSpriteColors == null)
        {
            return;
        }

        int count = Mathf.Min(damageFlashRenderers.Length, baseSpriteColors.Length);
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer sr = damageFlashRenderers[i];
            if (sr != null)
            {
                sr.color = baseSpriteColors[i];
            }
        }
    }

    protected ulong lastDamagerId = ulong.MaxValue;

    public virtual void TakeDamage(float dmg, ulong shooterId = ulong.MaxValue)
    {
        if (!IsServer) return;
        if (IsDead.Value) return;

        float safeDamage = Mathf.Max(0f, dmg);
        if (safeDamage <= 0f || Health.Value <= 0f)
        {
            return;
        }

        lastDamagerId = shooterId;

        Health.Value = Mathf.Max(0f, Health.Value - safeDamage);

        if (Health.Value <= 0)
        {
            Die();
        }
    }

    protected virtual void Die()
    {
        Debug.Log($"{OwnerClientId} died");

        IsDead.Value = true;

        if (!despawnOnDeath)
        {
            return;
        }

        NetworkObject netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn();
        }
    }

    protected void SetDespawnOnDeath(bool value)
    {
        despawnOnDeath = value;
    }

    public virtual void ServerRespawn(Vector3 worldPosition)
    {
        if (!IsServer) return;

        transform.position = worldPosition;
        Health.Value = Mathf.Max(1f, maxHealth);
        IsDead.Value = false;
    }
}