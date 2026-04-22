using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class WaveManager : NetworkBehaviour
{
    [Header("Spawn Setup")]
    [SerializeField] private NetworkObject enemyPrefab;
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private Transform[] playerRespawnPoints;

    [Header("Wave Setup")]
    [SerializeField] private int maxWaves = 5;
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

    public int MaxWaves => Mathf.Clamp(maxWaves, 1, 5);

    private Coroutine waveRoutine;

    private void OnValidate()
    {
        maxWaves = Mathf.Clamp(maxWaves, 1, 5);
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

        if (enemyPrefab == null)
        {
            Debug.LogWarning("WaveManager: enemyPrefab is missing.");
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

    private IEnumerator RunWavesRoutine()
    {
        CurrentWave.Value = 0;
        IsWaveRunning.Value = true;

        if (firstWaveDelay > 0f)
        {
            yield return new WaitForSeconds(firstWaveDelay);
        }

        int totalWaves = Mathf.Clamp(maxWaves, 1, 5);
        for (int wave = 1; wave <= totalWaves; wave++)
        {
            CurrentWave.Value = wave;
            RespawnDeadPlayers();

            int enemiesToSpawn = baseEnemiesPerWave + ((wave - 1) * extraEnemiesPerWave);
            yield return StartCoroutine(SpawnWaveRoutine(enemiesToSpawn));

            yield return StartCoroutine(WaitUntilWaveClearedRoutine());

            if (wave < totalWaves && timeBetweenWaves > 0f)
            {
                yield return new WaitForSeconds(timeBetweenWaves);
            }
        }

        IsWaveRunning.Value = false;
        waveRoutine = null;
        Debug.Log("WaveManager: all waves completed.");
    }

    private IEnumerator SpawnWaveRoutine(int enemyCount)
    {
        int safeCount = Mathf.Max(0, enemyCount);
        if (safeCount <= 0)
        {
            yield break;
        }

        float spawnDelay = Mathf.Max(1f, timeBetweenEnemySpawns);

        for (int i = 0; i < safeCount; i++)
        {
            SpawnOneEnemy();

            if (i < safeCount - 1)
            {
                yield return new WaitForSeconds(spawnDelay);
            }
        }
    }

    private void SpawnOneEnemy()
    {
        if (!IsServer) return;
        if (enemyPrefab == null || spawnPoints == null || spawnPoints.Length == 0) return;

        int index = Random.Range(0, spawnPoints.Length);
        Transform point = spawnPoints[index];
        if (point == null) return;

        NetworkObject enemyInstance = Instantiate(
            enemyPrefab,
            point.position,
            point.rotation
        );

        enemyInstance.Spawn(true);
    }

    private IEnumerator WaitUntilWaveClearedRoutine()
    {
        while (GetAliveEnemyCount() > 0)
        {
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
