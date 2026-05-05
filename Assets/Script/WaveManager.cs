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
    [SerializeField] private float timeBetweenEnemySpawns = 0.25f;
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

    private Coroutine waveRoutine;
    private int unspawnedEnemies = 0;
    private float nextAliveCheckTime;

    private void OnValidate()
    {
        baseEnemiesPerWave = Mathf.Max(1, baseEnemiesPerWave);
        extraEnemiesPerWave = Mathf.Max(0, extraEnemiesPerWave);
        timeBetweenEnemySpawns = Mathf.Max(0.01f, timeBetweenEnemySpawns);
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

            if (timeBetweenWaves > 0f)
            {
                yield return new WaitForSeconds(timeBetweenWaves);
            }

            wave++;
        }
    }

    private IEnumerator SpawnWaveRoutine(int enemyCount)
    {
        unspawnedEnemies = Mathf.Max(0, enemyCount);
        if (unspawnedEnemies <= 0)
        {
            yield break;
        }

        float spawnDelay = Mathf.Max(0.5f, timeBetweenEnemySpawns);

        while (unspawnedEnemies > 0)
        {
            SpawnOneEnemy();
            unspawnedEnemies--;

            if (unspawnedEnemies > 0)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }
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
