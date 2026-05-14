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
    public float LocalPredictedHealth { get; set; }
    public NetworkVariable<bool> IsDead =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public float Speed { get; protected set; }
    public float MaxHealth => maxHealth;
    protected virtual SoundType DeathSoundType => SoundType.PlayerDeath;
    private Color[] baseSpriteColors;
    private Coroutine damageFlashRoutine;

    protected void SetMaxHealth(float value)
    {
        maxHealth = Mathf.Max(1f, value);
    }

    public override void OnNetworkSpawn()
    {
        ResolveDamageFlashRenderers();
        Health.OnValueChanged += OnHealthChanged;

        LocalPredictedHealth = Health.Value;

        if (!IsServer) return;

        Health.Value = Mathf.Max(1f, maxHealth);
        LocalPredictedHealth = Health.Value;
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
        // Re-sync local predicted health with authoritative server health, 
        // but only if the server health is lower (to prevent ping-ponging during predicting rapid fire)
        // or if it's a heal/respawn
        if (newValue > previousValue || newValue < LocalPredictedHealth)
        {
            LocalPredictedHealth = newValue;
        }

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

    public void PlayLocalDamageFlash()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        PlayDamageFlash();
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

    [ServerRpc(RequireOwnership = false)]
    public void NotifyHitServerRpc(float dmg, ulong shooterId)
    {
        TakeDamage(dmg, shooterId);
    }

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

        // Play damage sound for all clients
        PlayDamageSoundClientRpc(transform.position, SoundType.BulletHit);

        if (Health.Value <= 0)
        {
            Die();
        }
    }

    protected virtual void Die()
    {
        Debug.Log($"{OwnerClientId} died");

        IsDead.Value = true;

        // Play death sound for all clients
        PlayDeathSoundClientRpc(transform.position);

        if (!despawnOnDeath)
        {
            // If we don't despawn, make sure to hide it visually on the server/host too when predicted
            gameObject.SetActive(false);
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
        LocalPredictedHealth = Health.Value;
        IsDead.Value = false;

        // Ensure object is visually re-enabled if it was hidden by client prediction
        gameObject.SetActive(true);
    }

    [ClientRpc]
    protected void PlayDamageSoundClientRpc(Vector3 worldPosition, SoundType damageSound)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(damageSound, worldPosition);
        }
    }

    [ClientRpc]
    protected void PlayDeathSoundClientRpc(Vector3 worldPosition)
    {        // Don't play the sound again if the client already played it locally via prediction
        if (!gameObject.activeSelf && IsClient && !IsServer) return;
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(DeathSoundType, worldPosition);
        }
    }
}