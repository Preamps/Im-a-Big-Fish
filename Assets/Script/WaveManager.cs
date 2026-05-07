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
        if (waveRoutine != null)
        {
            StopCoroutine(waveRoutine);
            waveRoutine = null;
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
            RespawnDeadPlayers();

            int enemiesToSpawn = baseEnemiesPerWave + ((wave - 1) * extraEnemiesPerWave);
            yield return StartCoroutine(SpawnWaveRoutine(enemiesToSpawn));

            yield return StartCoroutine(WaitUntilWaveClearedRoutine());

            // notify clients that the wave was cleared (increment signal)
            if (IsServer)
            {
                WaveClearedSignal.Value = WaveClearedSignal.Value + 1;
            }

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
        Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
        if (players == null || players.Length == 0)
        {
            return;
        }

        int respawnIndex = 0;
        for (int i = 0; i < players.Length; i++)
        {
            Player player = players[i];
            if (player == null || !player.IsSpawned || !player.IsDead.Value)
            {
                continue;
            }

            Vector3 respawnPos = GetPlayerRespawnPosition(respawnIndex);
            player.ServerRespawn(respawnPos);
            respawnIndex++;
        }
    }

    private Vector3 GetPlayerRespawnPosition(int respawnIndex)
    {
        if (playerRespawnPoints != null && playerRespawnPoints.Length > 0)
        {
            int index = Mathf.Abs(respawnIndex) % playerRespawnPoints.Length;
            Transform point = playerRespawnPoints[index];
            if (point != null)
            {
                return point.position;
            }
        }

        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            Transform point = spawnPoints[0];
            if (point != null)
            {
                return point.position;
            }
        }

        return Vector3.zero;
    }
}
