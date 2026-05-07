using UnityEngine;
using Unity.Netcode;
using TMPro;
using System.Collections.Generic;

public class Player : Character
{
    private static readonly List<Player> activePlayers = new List<Player>();

    public static IReadOnlyList<Player> ActivePlayers => activePlayers;

    [SerializeField] private float networkSendRate = 20f;
    [SerializeField] private float inputSendThreshold = 0.02f;
    [SerializeField] private float positionSendThreshold = 0.02f;
    [SerializeField] private float remoteSmoothTime = 0.08f;
    [SerializeField] private float jumpForce = 12f;
    [SerializeField] private Sprite downSprite;

    [Header("HP Regen Settings")]
    [SerializeField] private float hpRegenDelay = 5f;
    [SerializeField] private float hpRegenRate = 10f; // HP per second

    private Rigidbody2D rb;
    private Animator animator;
    private PhysicsMaterial2D noFrictionMaterial;

    private float moveInput;
    private bool wantsToJump;
    private float nextInputSendTime;
    private float nextPositionSendTime;
    private float lastSentMoveInput;
    private Vector2 lastSentPosition;
    private bool hasSentMoveInput;
    private bool hasSentPosition;
    private float lastDamageTime = -100f;
    private Vector2 remoteSmoothVelocity;
    private Collider2D[] playerColliders;
    private SpriteRenderer[] playerRenderers;
    private Gun[] playerGuns;
    private GunPickup nearbyPickup;
    public GunPickup NearbyPickup => nearbyPickup;

    private PurchasableBlockade nearbyBlockade;
    public PurchasableBlockade NearbyBlockade => nearbyBlockade;

    private Player nearbyDownedPlayer;
    public Player NearbyDownedPlayer => nearbyDownedPlayer;

    private SpriteRenderer mainRenderer;
    private Sprite originalSprite;
    private NetworkVariable<int> characterIndex = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    [Header("Character Cosmetics")]
    [SerializeField] private RuntimeAnimatorController[] characterAnimators = new RuntimeAnimatorController[4];
    [SerializeField] private Sprite[] characterDownSprites = new Sprite[4];

    // ⭐ sync movement ให้ทุก client
    private NetworkVariable<float> netMoveInput =
        new NetworkVariable<float>(0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);
    private NetworkVariable<Vector2> netPosition =
        new NetworkVariable<Vector2>(
            Vector2.zero,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    public NetworkVariable<Unity.Collections.FixedString32Bytes> playerName =
        new NetworkVariable<Unity.Collections.FixedString32Bytes>(
            "",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner
        );

    public NetworkVariable<int> Money =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public NetworkVariable<int> KillCount =
        new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    public NetworkVariable<bool> IsDown =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    [Header("UI")]
    [SerializeField] private TMP_Text nameText;

    private CameraFollow camFollow;

    private bool isInLobby = false;
    private float downTimer = 0f;

    private void Awake()
    {
        SetDespawnOnDeath(false);

        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        playerColliders = GetComponentsInChildren<Collider2D>(true);
        playerRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        playerGuns = GetComponentsInChildren<Gun>(true);
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        noFrictionMaterial = new PhysicsMaterial2D("PlayerNoFriction")
        {
            friction = 0f,
            bounciness = 0f
        };

        for (int i = 0; i < playerColliders.Length; i++)
        {
            if (playerColliders[i] != null)
            {
                playerColliders[i].sharedMaterial = noFrictionMaterial;
            }
        }

        mainRenderer = GetComponent<SpriteRenderer>();
        if (mainRenderer != null)
        {
            originalSprite = mainRenderer.sprite;
        }

        Speed = 4f;
    }

    private void Start()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        CheckLobbyState(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        CheckLobbyState(scene.name);
    }

    private void CheckLobbyState(string sceneName)
    {
        isInLobby = (sceneName != "GameScene");
        UpdateVisibilityState();
    }

    private void UpdateVisibilityState()
    {
        bool hidePlayer = isInLobby || IsDead.Value;
        ApplyDeadState(hidePlayer);
        ApplyDownState(IsDown.Value);
    }

    private void Update()
    {
        if (isInLobby) return;

        if (IsServer && !IsDead.Value && IsDown.Value)
        {
            downTimer -= Time.deltaTime;
            if (downTimer <= 0f)
            {
                base.Die();
            }
        }

        if (IsServer && !IsDead.Value && !IsDown.Value)
        {
            if (Time.time - lastDamageTime >= hpRegenDelay && Health.Value < MaxHealth)
            {
                Health.Value = Mathf.Min(MaxHealth, Health.Value + hpRegenRate * Time.deltaTime);
            }
        }

        if (IsDead.Value)
        {
            return;
        }

        // Owner อ่าน input เท่านั้น
        if (IsOwner)
        {
            if (camFollow == null)
            {
                camFollow = Object.FindFirstObjectByType<CameraFollow>();
                if (camFollow != null)
                {
                    camFollow.target = transform;
                }
            }

            HandleInput();
        }
        else
        {
            SmoothRemoteMovement();
        }

        if (IsDown.Value) return;

        // ทุกเครื่องเล่น animation
        HandleAnimation();
        Flip();
    }

    private void FixedUpdate()
    {
        if (isInLobby) return;

        if (IsDead.Value || IsDown.Value)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (!IsOwner) return;

        Move();
        FindNearbyDownedPlayers();
    }

    private void FindNearbyDownedPlayers()
    {
        float closestDist = 2.5f; // revive interaction range
        Player nearestP = null;

        Player[] allPlayers = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player p in allPlayers)
        {
            if (p == this) continue;
            if (p.IsDown.Value && !p.IsDead.Value)
            {
                float dist = Vector2.Distance(transform.position, p.transform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    nearestP = p;
                }
            }
        }
        nearbyDownedPlayer = nearestP;
    }


    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!activePlayers.Contains(this))
        {
            activePlayers.Add(this);
        }
        IsDead.OnValueChanged += OnDeadStateChanged;
        IsDown.OnValueChanged += OnDownStateChanged;
        playerName.OnValueChanged += OnPlayerNameChanged;
        characterIndex.OnValueChanged += OnCharacterIndexChanged;
        UpdateVisibilityState();
        UpdateNameUI(playerName.Value);

        // Ensure current index is applied immediately (in case value was set before listener ran)
        ApplyCharacterAppearance(characterIndex.Value);

        if (IsOwner)
        {
            playerName.Value = GameData.Instance.PlayerName;
            // Owner writes the chosen character index directly so it syncs to server/other clients
            characterIndex.Value = Mathf.Clamp(GameData.Instance.SelectedCharacterIndex, 0, GameData.CharacterCount - 1);
            netMoveInput.Value = moveInput;
            netPosition.Value = rb.position;
            lastSentMoveInput = moveInput;
            lastSentPosition = rb.position;
            hasSentMoveInput = true;
            hasSentPosition = true;
        }
        else
        {
            rb.position = netPosition.Value;
        }

        // Ignore collisions with all other currently spawned players
        Player[] allPlayers = Object.FindObjectsByType<Player>(FindObjectsSortMode.None);
        foreach (Player p in allPlayers)
        {
            if (p == this) continue;
            if (p.playerColliders == null || this.playerColliders == null) continue;

            foreach (var myCol in this.playerColliders)
            {
                foreach (var otherCol in p.playerColliders)
                {
                    if (myCol != null && otherCol != null && myCol.gameObject.activeInHierarchy && otherCol.gameObject.activeInHierarchy)
                    {
                        Physics2D.IgnoreCollision(myCol, otherCol, true);
                    }
                }
            }
        }

        // Ignore collisions with all currently spawned enemies
        Enemy[] allEnemies = Object.FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        foreach (Enemy e in allEnemies)
        {
            Collider2D eCol = e.GetComponent<Collider2D>();
            if (eCol == null || this.playerColliders == null) continue;

            foreach (var myCol in this.playerColliders)
            {
                if (myCol != null && myCol.gameObject.activeInHierarchy && e.gameObject.activeInHierarchy)
                {
                    Physics2D.IgnoreCollision(myCol, eCol, true);
                }
            }
        }

        if (!IsOwner) return;

        camFollow = Object.FindFirstObjectByType<CameraFollow>();
        if (camFollow != null)
        {
            camFollow.target = transform;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        activePlayers.Remove(this);
        IsDead.OnValueChanged -= OnDeadStateChanged;
        IsDown.OnValueChanged -= OnDownStateChanged;
        playerName.OnValueChanged -= OnPlayerNameChanged;
        characterIndex.OnValueChanged -= OnCharacterIndexChanged;
    }

    private void OnPlayerNameChanged(Unity.Collections.FixedString32Bytes previousValue, Unity.Collections.FixedString32Bytes newValue)
    {
        UpdateNameUI(newValue);
    }

    private void OnCharacterIndexChanged(int previousValue, int newValue)
    {
        Debug.Log($"[Player] CharacterIndex changed from {previousValue} to {newValue} on {(IsServer ? "Server" : "Client")}, Owner:{OwnerClientId}");
        ApplyCharacterAppearance(newValue);
    }

    [ServerRpc]
    private void RequestCharacterIndexServerRpc(int index, ServerRpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        Debug.Log($"[Player] Received RequestCharacterIndexServerRpc from client {sender} with index {index}");
        characterIndex.Value = Mathf.Clamp(index, 0, GameData.CharacterCount - 1);
    }

    private void ApplyCharacterAppearance(int index)
    {
        int safeIndex = Mathf.Clamp(index, 0, GameData.CharacterCount - 1);
        bool appliedAnimator = false;
        bool appliedDownSprite = false;

        if (animator != null && characterAnimators != null && safeIndex < characterAnimators.Length)
        {
            var controller = characterAnimators[safeIndex];
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                animator.Update(0f);
                appliedAnimator = true;
            }
        }

        if (characterDownSprites != null && safeIndex < characterDownSprites.Length)
        {
            var ds = characterDownSprites[safeIndex];
            if (ds != null)
            {
                downSprite = ds;
                appliedDownSprite = true;
            }
        }

        if (mainRenderer != null)
        {
            originalSprite = mainRenderer.sprite;
        }

        Debug.Log($"[Player] ApplyCharacterAppearance index={safeIndex} appliedAnimator={appliedAnimator} appliedDownSprite={appliedDownSprite} on {(IsServer ? "Server" : "Client")} Owner:{OwnerClientId}");

        UpdateVisibilityState();
    }

    private void UpdateNameUI(Unity.Collections.FixedString32Bytes newName)
    {
        if (nameText != null)
        {
            nameText.text = newName.ToString();
        }
    }

    public override void Move()
    {
        float newVelocityY = rb.linearVelocity.y;

        if (wantsToJump)
        {
            if (CheckGrounded())
            {
                newVelocityY = jumpForce;
            }
            wantsToJump = false; // Reset the jump request after consuming it
        }

        rb.linearVelocity = new Vector2(
            moveInput * Speed,
            newVelocityY
        );

        PublishPosition();
    }

    private bool CheckGrounded()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col != null)
        {
            // Set the check point just slightly above the bottom of the collider to raycast down
            Vector2 bottomCenter = new Vector2(col.bounds.center.x, col.bounds.min.y + 0.05f);

            // Check a short distance downward
            RaycastHit2D[] hits = Physics2D.RaycastAll(bottomCenter, Vector2.down, 0.15f);
            foreach (var hit in hits)
            {
                if (hit.collider == null || hit.collider.isTrigger)
                {
                    continue;
                }

                if (hit.collider.GetComponentInParent<Player>() != null)
                {
                    continue;
                }

                // Require a surface that actually faces upward so side wall contacts do not count as grounded.
                if (hit.normal.y > 0.5f)
                {
                    return true;
                }
            }
            return false;
        }

        // Fallback if no collider is somehow available
        return Mathf.Abs(rb.linearVelocity.y) < 0.01f;
    }

    void HandleInput()
    {
        moveInput = Input.GetAxisRaw("Horizontal");

        if (Input.GetKeyDown(KeyCode.Space))
        {
            wantsToJump = true;
        }

        if (IsDown.Value)
        {
            moveInput = 0f;
            return; // Can't do anything else while down
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (nearbyPickup != null)
            {
                NetworkObject pickupNetObj = nearbyPickup.GetComponent<NetworkObject>();
                if (pickupNetObj != null)
                {
                    BuyGunServerRpc(pickupNetObj.NetworkObjectId);
                }
            }
            else if (nearbyBlockade != null)
            {
                NetworkObject blockadeNetObj = nearbyBlockade.GetComponent<NetworkObject>();
                if (blockadeNetObj != null)
                {
                    BuyBlockadeServerRpc(blockadeNetObj.NetworkObjectId);
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.F) && nearbyDownedPlayer != null && nearbyDownedPlayer.IsDown.Value)
        {
            ReviveServerRpc(nearbyDownedPlayer.OwnerClientId);
        }

        float safeRate = Mathf.Max(1f, networkSendRate);
        if (Time.time < nextInputSendTime)
        {
            return;
        }

        bool changedEnough = !hasSentMoveInput || Mathf.Abs(moveInput - lastSentMoveInput) >= inputSendThreshold;
        if (!changedEnough)
        {
            return;
        }

        // ⭐ ส่งค่าไป network
        netMoveInput.Value = moveInput;
        lastSentMoveInput = moveInput;
        hasSentMoveInput = true;
        nextInputSendTime = Time.time + (1f / safeRate);
    }

    void PublishPosition()
    {
        float safeRate = Mathf.Max(1f, networkSendRate);
        if (Time.time < nextPositionSendTime)
        {
            return;
        }

        Vector2 currentPosition = rb.position;
        bool changedEnough = !hasSentPosition || Vector2.Distance(currentPosition, lastSentPosition) >= positionSendThreshold;
        if (!changedEnough)
        {
            return;
        }

        netPosition.Value = currentPosition;
        lastSentPosition = currentPosition;
        hasSentPosition = true;
        nextPositionSendTime = Time.time + (1f / safeRate);
    }

    void SmoothRemoteMovement()
    {
        Vector2 targetPosition = netPosition.Value;
        rb.position = Vector2.SmoothDamp(
            rb.position,
            targetPosition,
            ref remoteSmoothVelocity,
            Mathf.Max(0.01f, remoteSmoothTime)
        );
    }

    void HandleAnimation()
    {
        float speed = Mathf.Abs(netMoveInput.Value);
        animator.SetFloat("Speed", speed);
    }

    void Flip()
    {
        Vector3 scale = transform.localScale;

        float dir = netMoveInput.Value;

        if (dir > 0)
            scale.x = -Mathf.Abs(scale.x);
        else if (dir < 0)
            scale.x = Mathf.Abs(scale.x);

        transform.localScale = scale;

        // Counter-flip the name text so it stays readable (not mirrored)
        if (nameText != null)
        {
            Vector3 textScale = nameText.transform.localScale;
            textScale.x = Mathf.Abs(textScale.x) * Mathf.Sign(scale.x);
            nameText.transform.localScale = textScale;
        }
    }

    private void OnTriggerEnter2D(Collider2D col)
    {
        if (!IsOwner) return;
        GunPickup pickup = col.GetComponent<GunPickup>();
        if (pickup != null)
        {
            nearbyPickup = pickup;
        }

        PurchasableBlockade blockade = col.GetComponent<PurchasableBlockade>();
        if (blockade != null)
        {
            nearbyBlockade = blockade;
        }
    }

    private void OnTriggerExit2D(Collider2D col)
    {
        if (!IsOwner) return;
        if (nearbyPickup != null && col.gameObject == nearbyPickup.gameObject)
        {
            nearbyPickup = null;
        }

        if (nearbyBlockade != null && col.gameObject == nearbyBlockade.gameObject)
        {
            nearbyBlockade = null;
        }
    }

    private void OnCollisionEnter2D(Collision2D col)
    {
        if (!IsOwner) return;
        PurchasableBlockade blockade = col.gameObject.GetComponent<PurchasableBlockade>();
        if (blockade != null)
        {
            nearbyBlockade = blockade;
        }
    }

    private void OnCollisionExit2D(Collision2D col)
    {
        if (!IsOwner) return;
        if (nearbyBlockade != null && col.gameObject == nearbyBlockade.gameObject)
        {
            nearbyBlockade = null;
        }
    }

    [ServerRpc]
    private void BuyBlockadeServerRpc(ulong blockadeNetworkObjectId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(blockadeNetworkObjectId, out NetworkObject blockadeObj)) return;

        PurchasableBlockade blockade = blockadeObj.GetComponent<PurchasableBlockade>();
        if (blockade == null || Money.Value < blockade.price) return;

        Money.Value -= blockade.price;

        blockadeObj.Despawn(true);
    }

    [ServerRpc]
    private void BuyGunServerRpc(ulong pickupNetworkObjectId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(pickupNetworkObjectId, out NetworkObject pickupObj)) return;

        GunPickup pickup = pickupObj.GetComponent<GunPickup>();
        if (pickup == null || pickup.gunPrefab == null || Money.Value < pickup.price) return;

        Money.Value -= pickup.price;

        // Find existing gun and mount point
        Gun oldGun = GetComponentInChildren<Gun>(true);
        Transform mountPoint = transform;
        if (oldGun != null)
        {
            mountPoint = oldGun.transform.parent;
            NetworkObject oldGunNetObj = oldGun.GetComponent<NetworkObject>();
            if (oldGunNetObj != null && oldGunNetObj.IsSpawned)
            {
                oldGunNetObj.Despawn();
            }
            else
            {
                Destroy(oldGun.gameObject);
                DestroyNonNetworkedGunsClientRpc();
            }
        }

        // Spawn new gun
        GameObject newGun = Instantiate(pickup.gunPrefab, mountPoint.position, mountPoint.rotation);
        NetworkObject netObj = newGun.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.SpawnWithOwnership(OwnerClientId);
            netObj.TrySetParent(mountPoint, false);
        }
        else
        {
            newGun.transform.SetParent(mountPoint, false);
        }

        // Ensure the local transform perfectly matches the prefab to inherit player's current flip properly
        newGun.transform.localPosition = pickup.gunPrefab.transform.localPosition;
        newGun.transform.localRotation = pickup.gunPrefab.transform.localRotation;
        newGun.transform.localScale = pickup.gunPrefab.transform.localScale;

        UpdatePlayerGunsClientRpc();
    }

    [ClientRpc]
    private void DestroyNonNetworkedGunsClientRpc()
    {
        if (IsServer) return; // Server already destroyed it locally
        Gun[] existingGuns = GetComponentsInChildren<Gun>(true);
        foreach (Gun g in existingGuns)
        {
            NetworkObject netObj = g.GetComponent<NetworkObject>();
            // If it's a baked-in gun (no network object, or not properly spawned), destroy it manually on client
            if (netObj == null || !netObj.IsSpawned)
            {
                Destroy(g.gameObject);
            }
        }
    }

    [ClientRpc]
    private void UpdatePlayerGunsClientRpc()
    {
        playerGuns = GetComponentsInChildren<Gun>(true);
        playerRenderers = GetComponentsInChildren<SpriteRenderer>(true);
        UpdateVisibilityState(); // Re-apply visual state to new gun
    }

    private void OnDeadStateChanged(bool previousValue, bool newValue)
    {
        UpdateVisibilityState();
    }

    private void OnDownStateChanged(bool previousValue, bool newValue)
    {
        UpdateVisibilityState();
    }

    private void ApplyDownState(bool isDown)
    {
        if (IsDead.Value || isInLobby) return; // Dead or lobby overrides down visually

        if (animator != null)
        {
            animator.enabled = !isDown;
        }

        if (mainRenderer != null)
        {
            if (isDown && downSprite != null)
            {
                mainRenderer.sprite = downSprite;
            }
            else
            {
                mainRenderer.sprite = originalSprite;
            }
        }

        for (int i = 0; i < playerGuns.Length; i++)
        {
            if (playerGuns[i] != null)
            {
                playerGuns[i].enabled = !isDown;
                SpriteRenderer[] gunRenderers = playerGuns[i].GetComponentsInChildren<SpriteRenderer>();
                foreach (var r in gunRenderers)
                {
                    r.enabled = !isDown;
                }
            }
        }
    }

    private void ApplyDeadState(bool isDead)
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.simulated = !isDead;
        }

        if (animator != null)
        {
            animator.enabled = !isDead;
        }

        for (int i = 0; i < playerColliders.Length; i++)
        {
            if (playerColliders[i] != null)
            {
                playerColliders[i].enabled = !isDead;
            }
        }

        for (int i = 0; i < playerRenderers.Length; i++)
        {
            if (playerRenderers[i] != null)
            {
                playerRenderers[i].enabled = !isDead;
            }
        }

        for (int i = 0; i < playerGuns.Length; i++)
        {
            if (playerGuns[i] != null)
            {
                playerGuns[i].enabled = !isDead;
            }
        }

        if (nameText != null)
        {
            nameText.enabled = !isDead;
        }
    }

    public override void TakeDamage(float dmg, ulong shooterId = ulong.MaxValue)
    {
        if (!IsServer) return;
        if (IsDead.Value) return;

        float safeDamage = Mathf.Max(0f, dmg);
        if (safeDamage <= 0f || Health.Value <= 0f)
        {
            return;
        }

        lastDamageTime = Time.time;
        lastDamagerId = shooterId;
        Health.Value = Mathf.Max(0f, Health.Value - safeDamage);

        if (Health.Value <= 0)
        {
            if (!IsDown.Value)
            {
                IsDown.Value = true;
                downTimer = 15f; // Start down timer
                //Health.Value = 30f; // Give them some health to survive being down
            }
            else
            {
                base.Die();
            }
        }
    }

    protected override void Die()
    {
        if (!IsServer) return;

        if (!IsDown.Value)
        {
            IsDown.Value = true;
            downTimer = 15f;
            //Health.Value = 30f; // Survive as downed temporarily
            return; // don't call base.Die() yet
        }

        IsDown.Value = false;
        base.Die();
    }

    [ServerRpc]
    private void ReviveServerRpc(ulong downedPlayerId)
    {
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(downedPlayerId, out var client)) return;

        Player p = client.PlayerObject?.GetComponent<Player>();
        if (p != null && p.IsDown.Value)
        {
            p.Health.Value = 50f; // Revive health
            p.IsDown.Value = false;
        }
    }

    public override void ServerRespawn(Vector3 worldPosition)
    {
        IsDown.Value = false;
        base.ServerRespawn(worldPosition);
        ApplyRespawnClientRpc(worldPosition);
    }

    [ClientRpc]
    private void ApplyRespawnClientRpc(Vector3 worldPosition)
    {
        if (rb == null)
        {
            return;
        }

        rb.position = worldPosition;

        if (IsOwner)
        {
            netPosition.Value = worldPosition;
            lastSentPosition = worldPosition;
            hasSentPosition = true;
        }
    }
}