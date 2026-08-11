using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using Unity.Netcode;

[BepInPlugin("com.toii.overdosechaos", "Overdose Chaos", "1.0.0")]
public class OverdoseChaosPlugin : BaseUnityPlugin
{
    private readonly Harmony harmony = new Harmony("com.toii.overdosechaos");

    private void Awake()
    {
        harmony.PatchAll();
        Logger.LogInfo("Overdose Chaos est chargé et prêt à semer la panique !");
    }
}

[HarmonyPatch(typeof(RoundManager))]
public class OverdoseChaosManager
{
    public static bool hasPlayerEntered = false;
    public static float gameTimer = 0f;
    public static float monsterTimer = 0f;
    public static float objectTimer = 0f;

    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    static void ResetOnStart(RoundManager __instance)
    {
        hasPlayerEntered = false;
        gameTimer = 0f;
        monsterTimer = 0f;
        objectTimer = 0f;
    }

    [HarmonyPatch("Update")]
    [HarmonyPostfix]
    static void UpdateChaos(RoundManager __instance)
    {
        if (!hasPlayerEntered || __instance.currentLevel == null) return;
        
        float deltaTime = Time.deltaTime;
        gameTimer += deltaTime;
        monsterTimer += deltaTime;
        objectTimer += deltaTime;

        // 1. MONSTRES : 80% de chance toutes les 45 secondes (Limite de base + 6)
        if (monsterTimer >= 45f)
        {
            monsterTimer = 0f;
            
            int currentMonsters = Object.FindObjectsOfType<EnemyAI>().Length;
            int maxAllowedMonsters = __instance.currentLevel.maxEnemies + 6;

            if (currentMonsters < maxAllowedMonsters && Random.value <= 0.80f)
            {
                TrySpawnChaosEnemy(__instance);
            }
        }

        // 2. OBJETS : 96% de chance toutes les 25 secondes (Max 60 objets à l'intérieur)
        if (objectTimer >= 25f)
        {
            objectTimer = 0f;
            
            int currentScrapCount = Object.FindObjectsOfType<Griddable>().Length;
            
            if (currentScrapCount < 60 && Random.value <= 0.96f)
            {
                TrySpawnRandomScrap(__instance);
            }
        }

        CheckMonsterMeleeAttacks();
    }

    static void TrySpawnChaosEnemy(RoundManager manager)
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null || StartOfRound.Instance.allPlayerScripts.Length == 0) return;
        
        PlayerControllerB targetPlayer = StartOfRound.Instance.allPlayerScripts[Random.Range(0, StartOfRound.Instance.allPlayerScripts.Length)];
        if (targetPlayer == null || !targetPlayer.isPlayerControlled || targetPlayer.isPlayerDead) return;

        if (manager.currentLevel.Enemies == null || manager.currentLevel.Enemies.Count == 0) return;

        SpawnableEnemyWithRarity selectedEnemy = manager.currentLevel.Enemies[Random.Range(0, manager.currentLevel.Enemies.Count)];
        Vector3 spawnPos = targetPlayer.transform.position + (Random.insideUnitSphere * Random.Range(10f, 20f));

        NavMeshHit hit;
        if (NavMesh.SamplePosition(spawnPos, out hit, 15f, NavMesh.AllAreas))
        {
            int enemyIndex = manager.currentLevel.Enemies.IndexOf(selectedEnemy);
            if (enemyIndex != -1)
            {
                manager.SpawnEnemyOnServer(hit.position, 0f, enemyIndex);
            }
        }
    }

    static void TrySpawnRandomScrap(RoundManager manager)
    {
        if (manager.spawnableScrap == null || manager.spawnableScrap.Count == 0) return;

        SpawnableItemWithRarity selectedScrap = manager.spawnableScrap[Random.Range(0, manager.spawnableScrap.Count)];
        if (selectedScrap == null || selectedScrap.spawnableItem == null) return;

        if (manager.insideAINodes == null || manager.insideAINodes.Length == 0) return;

        Vector3 randomPos = manager.GetRandomNavMeshPositionInRadius(manager.insideAINodes[Random.Range(0, manager.insideAINodes.Length)].transform.position, 10f);

        GameObject droppedItem = Object.Instantiate(selectedScrap.spawnableItem.spawnPrefab, randomPos, Quaternion.identity);
        Griddable griddable = droppedItem.GetComponent<Griddable>();
        if (griddable != null)
        {
            griddable.GetItemDataAndSync();
            griddable.targetFloorPosition = randomPos;
            griddable.itemProperties = selectedScrap.spawnableItem;
        }

        PhysicsProp prop = droppedItem.GetComponentInChildren<PhysicsProp>();
        if (prop != null)
        {
            prop.fallTime = 0f;
        }

        NetworkObject netObj = droppedItem.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.Spawn();
        }
    }

    static void CheckMonsterMeleeAttacks()
    {
        if (StartOfRound.Instance == null || StartOfRound.Instance.allPlayerScripts == null) return;

        EnemyAI[] allEnemies = Object.FindObjectsOfType<EnemyAI>();
        foreach (EnemyAI enemy in allEnemies)
        {
            if (enemy == null || enemy.isEnemyDead) continue;

            foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
            {
                if (player == null || !player.isPlayerControlled || player.isPlayerDead) continue;

                float distance = Vector3.Distance(enemy.transform.position, player.transform.position);
                if (distance < 2.5f)
                {
                    if (enemy.currentBehaviourStateIndex != 1)
                    {
                        enemy.SwitchToBehaviourState(1);
                    }
                    player.DamagePlayer(20, true, true, CauseOfDeath.Mauling, 0);
                    return;
                }
            }
        }
    }
}
