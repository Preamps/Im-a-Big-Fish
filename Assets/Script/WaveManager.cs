using System.Collections;
using Unity.Netcode;
using UnityEngine;

[System.Serializable]
public struct EnemySpawnConfig
{
    public NetworkObject prefab;
    [Min(0f)] public float spawnWeight;
}

public class WaveManager : NetworkBehaviour
{
    [Header("Spawn Setup")]
    [SerializeField] private EnemySpawnConfig[] enemyConfigs;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private Transform[] playerRespawnPoints;
    [SerializeField] private GameObject playerPrefab;

    [Header("Wave Setup")]
    [SerializeField] private int baseEnemiesPerWave = 3;
    [SerializeField] private int extraEnemiesPerWave = 2;
    [SerializeField] private float enemyHealthIncreasePerWave = 5f;
    [SerializeField] private float timeBetweenEnemySpawns = 0.25f;
    [SerializeField] private float timeBetweenPhases = 5f;
    [SerializeField] private float timeBetweenWaves = 2f;
    [SerializeField] private float firstWaveDelay = 1f;
    [SerializeField] private float aliveCheckInterval = 0.2f;

    public NetworkVariable<int> CurrentWave = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<bool> IsWaveRunning = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> EnemiesRemaining = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public NetworkVariable<int> WaveClearedSignal = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine waveRoutine;
    private int unspawnedEnemies = 0;
    private float nextAliveCheckTime;

    private void OnValidate()
    {
        baseEnemiesPerWave = Mathf.Max(1, baseEnemiesPerWave);
        extraEnemiesPerWave = Mathf.Max(0, extraEnemiesPerWave);
        enemyHealthIncreasePerWave = Mathf.Max(0f, enemyHealthIncreasePerWave);
        timeBetweenEnemySpawns = Mathf.Max(0.01f, timeBetweenEnemySpawns);
        timeBetweenPhases = Mathf.Max(0f, timeBetweenPhases);
        timeBetweenWaves = Mathf.Max(0f, timeBetweenWaves);
        firstWaveDelay = Mathf.Max(0f, firstWaveDelay);
        aliveCheckInterval = Mathf.Max(0.05f, aliveCheckInterval);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;

        if (enemyConfigs == null || enemyConfigs.Length == 0)
        {
            Debug.LogWarning("WaveManager: add at least 1 enemy prefab.");
            return;
        }

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("WaveManager: add at least 1 spawn point.");
            return;
        }

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
        }

        waveRoutine = StartCoroutine(RunWavesRoutine());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer && NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
        {
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }

        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
        }
    }

    private void OnLoadEventCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, System.Collections.Generic.List<ulong> clientsCompleted, System.Collections.Generic.List<ulong> clientsTimedOut)
    {
        if (sceneName == "GameScene")
        {
            foreach (ulong clientId in clientsCompleted)
            {
                // 1. Server selects a spawn point
                Vector3 spawnPos = GetRandomPlayerRespawnPosition();

                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
                {
                    // If NetworkManager was allowed to spawn one automatically, we ignore it or use it, 
                    // but since we are writing custom logic:
                    if (client.PlayerObject == null && playerPrefab != null)
                    {
                        // 2. Instantiate the object at the spawn point
                        GameObject playerInstance = Instantiate(playerPrefab, spawnPos, Quaternion.identity);

                        // 3. Spawn the network object (Step 4 happens natively on Clients on spawn)
                        playerInstance.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);

                        Player p = playerInstance.GetComponent<Player>();
                        if (p != null) p.ServerRespawn(spawnPos);
                    }
                    else if (client.PlayerObject != null)
                    {
                        // Fallback just in case you haven't unchecked the default box yet
                        Player p = client.PlayerObject.GetComponent<Player>();
                        if (p != null) p.ServerRespawn(spawnPos);
                    }
                }
            }
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        if (IsWaveRunning.Value)
        {
            if (Time.time >= nextAliveCheckTime)
            {
                nextAliveCheckTime = Time.time + aliveCheckInterval;
                EnemiesRemaining.Value = unspawnedEnemies + GetAliveEnemyCount();
            }
        }
    }

    private IEnumerator RunWavesRoutine()
    {
        CurrentWave.Value = 0;
        IsWaveRunning.Value = true;

        if (firstWaveDelay > 0f)
        {
            yield return new WaitForSeconds(firstWaveDelay);
        }

        int wave = 1;
        while (true)
        {
            CurrentWave.Value = wave;
            AnnounceWaveStartClientRpc(wave);
            RespawnDeadPlayers();

            int enemiesToSpawn = baseEnemiesPerWave + ((wave - 1) * extraEnemiesPerWave);
            yield return StartCoroutine(SpawnWaveRoutine(enemiesToSpawn));

            yield return StartCoroutine(WaitUntilWaveClearedRoutine());

            // notify clients that the wave was cleared (increment signal)
            if (IsServer)
            {
                WaveClearedSignal.Value = WaveClearedSignal.Value + 1;
            }

            AnnounceWaveCompleteClientRpc(wave);

            if (timeBetweenWaves > 0f)
            {
                yield return new WaitForSeconds(timeBetweenWaves);
            }

            wave++;
        }
    }

    private IEnumerator SpawnWaveRoutine(int enemyCount)
    {
        if (enemyCount <= 0)
        {
            yield break;
        }

        float spawnDelay = Mathf.Max(0.01f, timeBetweenEnemySpawns);
        float phaseBetweenDelay = Mathf.Max(0f, timeBetweenPhases);

        // Calculate phase 1 and phase 2 counts
        int phase1Count = (enemyCount + 1) / 2; // Round up
        int phase2Count = enemyCount - phase1Count;

        // Phase 1: Spawn first half
        for (int i = 0; i < phase1Count; i++)
        {
            SpawnOneEnemy();
            unspawnedEnemies = enemyCount - i - 1;

            if (i < phase1Count - 1)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }

        // Wait between phases if there are enemies in phase 2
        if (phaseBetweenDelay > 0f && phase2Count > 0)
        {
            yield return new WaitForSeconds(phaseBetweenDelay);
        }

        // Phase 2: Spawn second half
        for (int i = 0; i < phase2Count; i++)
        {
            SpawnOneEnemy();
            unspawnedEnemies = phase2Count - i - 1;

            if (i < phase2Count - 1)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }

        unspawnedEnemies = 0;
    }

    private void SpawnOneEnemy()
    {
        if (!IsServer) return;
        if (enemyConfigs == null || enemyConfigs.Length == 0 || spawnPoints == null || spawnPoints.Length == 0) return;

        int index = Random.Range(0, spawnPoints.Length);
        Transform point = spawnPoints[index];
        if (point == null) return;

        float totalWeight = 0f;
        for (int i = 0; i < enemyConfigs.Length; i++)
        {
            if (enemyConfigs[i].prefab != null && enemyConfigs[i].spawnWeight > 0f)
            {
                totalWeight += enemyConfigs[i].spawnWeight;
            }
        }

        if (totalWeight <= 0f) return;

        float randomVal = Random.Range(0f, totalWeight);
        float currentWeight = 0f;
        NetworkObject prefabToSpawn = null;

        for (int i = 0; i < enemyConfigs.Length; i++)
        {
            if (enemyConfigs[i].prefab != null && enemyConfigs[i].spawnWeight > 0f)
            {
                currentWeight += enemyConfigs[i].spawnWeight;
                if (randomVal <= currentWeight)
                {
                    prefabToSpawn = enemyConfigs[i].prefab;
                    break;
                }
            }
        }

        if (prefabToSpawn == null) return;

        NetworkObject enemyInstance = Instantiate(
            prefabToSpawn,
            point.position,
            point.rotation
        );

        if (enemyInstance.TryGetComponent<Enemy>(out Enemy enemy))
        {
            enemy.ApplyWaveHealthBonus(CurrentWave.Value, enemyHealthIncreasePerWave);
        }

        enemyInstance.Spawn(true);
    }

    private IEnumerator WaitUntilWaveClearedRoutine()
    {
        while (true)
        {
            if (EnemiesRemaining.Value <= 0)
            {
                break;
            }
            yield return new WaitForSeconds(aliveCheckInterval);
        }
    }

    private int GetAliveEnemyCount()
    {
        Enemy[] enemies = FindObjectsByType<Enemy>(FindObjectsSortMode.None);
        int alive = 0;

        for (int i = 0; i < enemies.Length; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy == null) continue;
            if (!enemy.IsSpawned) continue;
            if (enemy.Health.Value <= 0f) continue;

            alive++;
        }

        return alive;
    }

    private void RespawnDeadPlayers()
    {
        Player[] players = FindObjectsByType<Player>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            // Players might just be down instead of dead. Respawn them either way.
            if (player == null || !player.IsSpawned || (!player.IsDead.Value && !player.IsDown.Value))
            {
                continue;
            }

            Vector3 respawnPos = GetRandomPlayerRespawnPosition();
            player.ServerRespawn(respawnPos);
        }
    }

    public Vector3 GetRandomPlayerRespawnPosition()
    {
        if (playerRespawnPoints != null && playerRespawnPoints.Length > 0)
        {
            int index = UnityEngine.Random.Range(0, playerRespawnPoints.Length);
            Transform point = playerRespawnPoints[index];
            if (point != null)
            {
                return point.position;
            }
        }
        return Vector3.zero;
    }

    [ClientRpc]
    private void AnnounceWaveStartClientRpc(int waveNumber)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(SoundType.WaveStart);
            SoundManager.Instance.PlayMusic(SoundType.WaveMusic);
        }
    }

    [ClientRpc]
    private void AnnounceWaveCompleteClientRpc(int waveNumber)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySound(SoundType.WaveComplete);
            SoundManager.Instance.PlayMusic(SoundType.BackgroundMusic);
        }
    }
}