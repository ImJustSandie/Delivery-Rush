using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;

/// <summary>
/// Gestor automatizado de la escena Podio.
/// - Ordena jugadores 1º→4º por puntuación (deliveredScore).
/// - Los coloca en PodiumSlots.
/// - Cada jugador salta y debajo se va formando una torre de bloques igual a su puntuación total.
/// Colocar este componente en un GameObject de la escena Podium (ej. PodiumManager).
/// Toda la lógica es local y determinista: cada cliente calcula el mismo ranking a partir de NetworkVariables sincronizadas.
/// </summary>
public class PodiumManager : MonoBehaviour
{
    [Header("Slots")]
    [Tooltip("4 Transforms para 1º,2º,3º,4º. Si está vacío, busca objetos PodiumSlot1..4 o genera posiciones por defecto.")]
    [SerializeField] private Transform[] podiumSlots = new Transform[4];
    [Tooltip("Altura base sobre el slot donde aparece el jugador.")]
    [SerializeField] private float playerHeightOffset = 2f;
    [Tooltip("Si no hay 4 slots, separación horizontal por defecto.")]
    [SerializeField] private float fallbackSpacing = 3f;

    [Header("Torre")]
    [Tooltip("Prefab del bloque de la torre. Si es null, se usa un Cube primitivo con material del collectible.")]
    [SerializeField] private GameObject towerBlockPrefab;
    [Tooltip("Altura de cada bloque.")]
    [SerializeField] private float blockHeight = 0.5f;
    [Tooltip("Tamaño del bloque (x,z).")]
    [SerializeField] private Vector3 blockSize = new Vector3(0.9f, 0.5f, 0.9f);
    [Tooltip("Intervalo entre bloques.")]
    [SerializeField] private float blockSpawnInterval = 0.22f;
    [Tooltip("Bloques por segundo en modo rápido (si puntuación es alta).")]
    [SerializeField] private float maxBuildDuration = 8f;

    [Header("Salto")]
    [Tooltip("Altura del salto.")]
    [SerializeField] private float jumpHeight = 1.1f;
    [Tooltip("Duración de cada salto.")]
    [SerializeField] private float jumpDuration = 0.45f;
    [Tooltip("Salto continuo mientras se construye, luego cada X seg.")]
    [SerializeField] private float idleJumpInterval = 1.2f;

    [Header("Ranking UI (opcional)")]
    [Tooltip("Textos opcionales para mostrar nombre/puntos en cada podio. Index 0=1º")]
    [SerializeField] private TMP_Text[] rankLabels;

    [Header("Camera Follow")]
    [Tooltip("Si es true, la cámara de Podio sube acompañando al jugador con más puntos.")]
    [SerializeField] private bool followCamera = true;
    [Tooltip("Offset de la cámara respecto a la cima de la torre más alta. Reducido para no dejar al jugador fuera de encuadre.")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 1.8f, -10f);
    [SerializeField] private float cameraFollowSpeed = 1.2f;
    [Tooltip("La cámara no empieza a subir hasta que se hayan colocado al menos estos bloques en total.")]
    [SerializeField] private int cameraFollowThresholdBlocks = 5;

    [Header("Debug")]
    [SerializeField] private bool logRanking = true;

    private readonly List<GameObject> spawnedBlocks = new List<GameObject>();
    private List<PlayerMovementManager> rankedPlayers = new List<PlayerMovementManager>();
    private Camera podiumCamera;
    private Vector3 podiumCameraInitialPos;
    private float podiumCameraTargetY;
    private float podiumCameraBaseY;

    private void Start()
    {
        StartCoroutine(SetupPodiumRoutine());
    }

    private void LateUpdate()
    {
        Camera cam = podiumCamera;
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null) cam = FindFirstObjectByType<Camera>();
            podiumCamera = cam;
        }
        if (cam != null)
        {
            // Rank labels mirando al frente de la cámara
            if (rankLabels != null)
            {
                foreach (TMP_Text label in rankLabels)
                {
                    if (label == null) continue;
                    label.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, cam.transform.up);
                }
            }

            // Cámara sigue la cima de la torre más alta (no la cúspide del salto) para evitar adelantarse
            if (followCamera && rankedPlayers != null && rankedPlayers.Count > 0)
            {
                if (spawnedBlocks.Count < cameraFollowThresholdBlocks) return;

                // Usar la altura de la torre (bloque más alto) en vez de la Y del jugador con salto, así no se adelanta
                float maxBlockY = float.MinValue;
                foreach (var b in spawnedBlocks)
                {
                    if (b == null) continue;
                    if (b.transform.position.y > maxBlockY) maxBlockY = b.transform.position.y;
                }
                float targetY;
                if (maxBlockY != float.MinValue)
                {
                    // Cima de la torre + offset del jugador + offset de cámara → jugador queda centrado, no abajo
                    targetY = maxBlockY + blockHeight * 0.5f + playerHeightOffset + cameraOffset.y;
                }
                else
                {
                    float maxY = float.MinValue;
                    foreach (var pm in rankedPlayers)
                    {
                        if (pm == null) continue;
                        // Quitar el pico del salto para no adelantar
                        float yWithoutJump = pm.transform.position.y - jumpHeight * 0.5f;
                        if (yWithoutJump > maxY) maxY = yWithoutJump;
                    }
                    if (maxY == float.MinValue) return;
                    targetY = Mathf.Max(podiumCameraBaseY, maxY + cameraOffset.y);
                }
                targetY = Mathf.Max(podiumCameraBaseY, targetY);
                Vector3 cur = cam.transform.position;
                Vector3 desired = new Vector3(cur.x, targetY, cur.z);
                cam.transform.position = Vector3.Lerp(cur, desired, Time.deltaTime * cameraFollowSpeed);
            }
        }
    }

    private IEnumerator SetupPodiumRoutine()
    {
        // Esperar a que los NetworkObjects de los jugadores se spawneen en la nueva escena
        yield return new WaitForSeconds(0.5f);

        // Reintentar hasta encontrar al menos 1 jugador (máx 5s)
        float timeout = 5f;
        while (timeout > 0f)
        {
            rankedPlayers = FindObjectsByType<PlayerMovementManager>(FindObjectsSortMode.None).ToList();
            if (rankedPlayers.Count > 0) break;
            timeout -= 0.2f;
            yield return new WaitForSeconds(0.2f);
        }

        if (rankedPlayers.Count == 0)
        {
            Debug.LogWarning("[PodiumManager] No se encontraron jugadores en Podio.");
            yield break;
        }

        ResolveSlots();
        RankPlayers();

        // Configurar cámara de podio: usar Main Camera y desactivar PlayerCameras para tener una sola vista
        podiumCamera = Camera.main;
        if (podiumCamera == null) podiumCamera = FindFirstObjectByType<Camera>();
        if (podiumCamera != null)
        {
            podiumCameraBaseY = podiumCamera.transform.position.y;
            podiumCameraTargetY = podiumCameraBaseY;
            // Desactivar todas las PlayerCamera (las del prefab) para que solo renderice la del podio
            foreach (var pm in rankedPlayers)
            {
                Camera pc = pm.GetComponentInChildren<Camera>(true);
                if (pc != null && pc != podiumCamera) pc.enabled = false;
                AudioListener al = pm.GetComponentInChildren<AudioListener>(true);
                if (al != null) al.enabled = false;
                var camCtrl = pm.GetComponentInChildren<CameraYawPitchDragController>(true);
                if (camCtrl != null) camCtrl.enabled = false;
            }
            // Asegurar que la cámara del podio esté activa y con AudioListener
            podiumCamera.enabled = true;
            var podiumListener = podiumCamera.GetComponent<AudioListener>();
            if (podiumListener == null) podiumCamera.gameObject.AddComponent<AudioListener>();
            else podiumListener.enabled = true;
        }

        // Colocar jugadores en sus slots
        for (int i = 0; i < rankedPlayers.Count && i < podiumSlots.Length; i++)
        {
            PlayerMovementManager pm = rankedPlayers[i];
            Transform slot = podiumSlots[i];
            TeleportPlayerToSlot(pm, slot);
            if (rankLabels != null && i < rankLabels.Length && rankLabels[i] != null)
            {
                string playerName = pm.TryGetComponent<NetworkPlayerSkinSynchronizer>(out var skinSync) && !string.IsNullOrWhiteSpace(skinSync.PlayerName)
                    ? skinSync.PlayerName 
                    : pm.name.Replace("(Clone)", "");
                int slotIndex = GetPlayerSlotIndex(pm);
                int playerSlot = slotIndex + 1;
                rankLabels[i].text = $"P{playerSlot}: {playerName}\nPuntos: {pm.Score}";

                if (pm.TryGetComponent<LobbyPlayerDisplay>(out var display))
                {
                    rankLabels[i].color = display.GetSlotColor(slotIndex);
                }
            }
        }

        if (logRanking)
        {
            string log = "[PodiumManager] Posiciones de Podio (por Slot de Jugador):\n";
            for (int i = 0; i < rankedPlayers.Count; i++)
                log += $"Slot {GetPlayerSlotIndex(rankedPlayers[i]) + 1} -> {rankedPlayers[i].name} - {rankedPlayers[i].Score} pts (lleva {rankedPlayers[i].CollectedCount})\n";
            Debug.Log(log);
        }

        // Iniciar animación de salto + torres para cada jugador (local, determinista)
        for (int i = 0; i < rankedPlayers.Count && i < podiumSlots.Length; i++)
        {
            StartCoroutine(BuildTowerAndJumpRoutine(rankedPlayers[i], podiumSlots[i]));
        }
    }

    private void ResolveSlots()
    {
        // Si hay slots asignados, ok. Si no, buscar por nombre PodiumSlot1..4
        bool hasAny = podiumSlots != null && podiumSlots.Any(t => t != null);
        if (hasAny) return;

        List<Transform> found = new List<Transform>();
        for (int i = 1; i <= 4; i++)
        {
            GameObject go = GameObject.Find($"PodiumSlot{i}");
            if (go == null) go = GameObject.Find($"PodiumSlot {i}");
            if (go == null) go = GameObject.Find($"Slot{i}");
            found.Add(go != null ? go.transform : null);
        }

        if (found.Any(t => t != null))
        {
            podiumSlots = found.ToArray();
            // Rellenar nulos con fallback
            for (int i = 0; i < podiumSlots.Length; i++)
                if (podiumSlots[i] == null)
                    podiumSlots[i] = CreateFallbackSlot(i);
            return;
        }

        // Generar 4 slots por defecto en línea, 1º al centro más alto
        podiumSlots = new Transform[4];
        for (int i = 0; i < 4; i++)
            podiumSlots[i] = CreateFallbackSlot(i);
    }

    private Transform CreateFallbackSlot(int index)
    {
        // Orden visual: 1º centro alto, 2º izq, 3º der, 4º fondo
        // Si el manager está muy lejos (ej. 2000+ unidades por error de escena), usar origen en vez de su posición
        Vector3 anchor = transform.position;
        if (anchor.magnitude > 1000f)
            anchor = Vector3.zero;

        // Intentar anclar cerca de la Main Camera del podio para que sea visible
        Camera cam = Camera.main;
        if (cam == null) cam = FindFirstObjectByType<Camera>();
        if (cam != null && Vector3.Distance(anchor, cam.transform.position) > 50f)
        {
            // Colocar slots frente a la cámara
            anchor = cam.transform.position + cam.transform.forward * 10f;
            anchor.y = 0f;
        }

        Vector3[] offsets = new Vector3[]
        {
            new Vector3(0f, 0f, 0f),          // 1º
            new Vector3(-fallbackSpacing, -0.5f, 0f), // 2º
            new Vector3(fallbackSpacing, -1f, 0f),  // 3º
            new Vector3(0f, -1.5f, -fallbackSpacing), // 4º
        };
        GameObject go = new GameObject($"PodiumSlot{index + 1}_Auto");
        go.transform.position = anchor + offsets[Mathf.Min(index, offsets.Length - 1)];
        go.transform.rotation = Quaternion.identity;
        return go.transform;
    }

    private int GetPlayerSlotIndex(PlayerMovementManager pm)
    {
        if (pm == null) return 0;
        if (pm.TryGetComponent<LobbyPlayerDisplay>(out var display))
        {
            return display.PlayerSlotIndex;
        }
        if (NetworkGameManager.Instance != null)
        {
            return NetworkGameManager.Instance.GetPlayerSlot(pm.OwnerClientId);
        }
        return (int)(pm.OwnerClientId % 4);
    }

    private void RankPlayers()
    {
        // Mantener a cada jugador en su posición/slot correspondiente como jugador (0, 1, 2, 3)
        rankedPlayers = rankedPlayers
            .OrderBy(p => GetPlayerSlotIndex(p))
            .ToList();
    }

    private void TeleportPlayerToSlot(PlayerMovementManager pm, Transform slot)
    {
        if (pm == null || slot == null) return;
        Vector3 pos = slot.position + Vector3.up * playerHeightOffset;
        Quaternion rot = slot.rotation;

        // Desactivar movimiento autónomo del jugador durante el podio para que no interfiera con la animación
        pm.enabled = false;

        // Desactivar CharacterController para teleport sin colisión
        CharacterController cc = pm.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        pm.transform.position = pos;
        pm.transform.rotation = rot;
        if (cc != null) cc.enabled = true;

        // En Podio no usar PlayerCamera: se desactiva en SetupPodiumRoutine y se usa la Main Camera del podio
    }

    private IEnumerator BuildTowerAndJumpRoutine(PlayerMovementManager player, Transform slot)
    {
        if (player == null || slot == null) yield break;
        // Total real = entregados + los que aún lleva encima (por si no alcanzó a entregar)
        int totalBlocks = player.Score + player.CollectedCount;
        if (totalBlocks > 0)
        {
            // Calcular intervalo adaptado para no tardar demasiado si hay muchos puntos
            float interval = blockSpawnInterval;
            if (totalBlocks * interval > maxBuildDuration)
                interval = maxBuildDuration / totalBlocks;

            Vector3 basePos = slot.position;
            GameObject blockPrefab = ResolveBlockPrefab();

            for (int i = 0; i < totalBlocks; i++)
            {
                Vector3 blockPos = basePos + Vector3.up * (i * blockHeight);

                // Spawnear bloque local sin físicas: solo visual apilado, no desplazable
                GameObject block = Instantiate(blockPrefab, blockPos, Quaternion.identity);
                block.name = $"Tower_{player.OwnerClientId}_{i}";
                block.transform.localScale = blockSize;
                MakeBlockStatic(block);
                spawnedBlocks.Add(block);

                // Animar exactamente 1 salto por bloque hasta la nueva altura
                Vector3 targetPlayerPos = basePos + Vector3.up * (playerHeightOffset + (i + 1) * blockHeight);
                yield return AnimateJump(player, targetPlayerPos);

                yield return new WaitForSeconds(interval);
            }

            Debug.Log($"[PodiumManager] {player.name} completó torre de {totalBlocks} bloques.");
        }
        else
        {
            Debug.Log($"[PodiumManager] {player.name} sin puntos, no salta.");
        }

        // Determinar si es 1er lugar para activar la animación adecuada
        TriggerPostJumpAnimation(player);
    }

    private void TriggerPostJumpAnimation(PlayerMovementManager player)
    {
        if (player == null) return;

        int maxScore = (rankedPlayers != null && rankedPlayers.Count > 0)
            ? rankedPlayers.Max(p => p != null ? p.Score + p.CollectedCount : 0)
            : 0;

        int playerBlocks = player.Score + player.CollectedCount;

        // Es primer lugar si tiene el máximo de puntos (y al menos 1 punto) o es el primero de la lista
        bool isFirstPlace = false;
        if (maxScore > 0)
        {
            isFirstPlace = (playerBlocks == maxScore);
        }
        else if (rankedPlayers != null && rankedPlayers.Count > 0)
        {
            isFirstPlace = (rankedPlayers[0] == player);
        }

        PlayerAnimationController animCtrl = player.GetComponentInChildren<PlayerAnimationController>();
        if (animCtrl != null)
        {
            animCtrl.PlayResultAnimation(isFirstPlace);
            Debug.Log($"[PodiumManager] Animación de resultado activada para {player.name}: {(isFirstPlace ? "Victoria / Celebración" : "Derrota")}");
        }
    }

    private GameObject ResolveBlockPrefab()
    {
        if (towerBlockPrefab != null) return towerBlockPrefab;

        // Fallback: crear Cube primitivo sin físicas
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        MakeBlockStatic(cube);
        Renderer r = cube.GetComponent<Renderer>();
        if (r != null)
        {
            r.material.color = new Color(0.95f, 0.85f, 0.4f);
        }
        cube.SetActive(false);
        towerBlockPrefab = cube;
        return towerBlockPrefab;
    }

    private void MakeBlockStatic(GameObject block)
    {
        // Eliminar toda física: sin Rigidbody, sin InteractableCube/NetworkObject, collider solo estático no desplazable
        Rigidbody rb = block.GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        // Si el prefab trae InteractableCube/Collectible, quitarlo para que no sea recolectable en podio
        var interactable = block.GetComponent<InteractableCube>();
        if (interactable != null) Destroy(interactable);
        var netObj = block.GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);
        // Asegurar que no sea trigger y no tenga físicas
        Collider col = block.GetComponent<Collider>();
        if (col != null) col.isTrigger = false;
        // Desactivar gravedad/físicas implícitas: static
        block.isStatic = true;
    }

    private IEnumerator AnimateJump(PlayerMovementManager player, Vector3 targetPos)
    {
        if (player == null) yield break;
        Vector3 startPos = player.transform.position;
        // Si ya está en target, solo hacer pequeño salto vertical
        if (Vector3.Distance(new Vector3(startPos.x, 0, startPos.z), new Vector3(targetPos.x, 0, targetPos.z)) < 0.01f)
        {
            // Salto vertical en sitio
            float t = 0f;
            Vector3 basePos = new Vector3(startPos.x, targetPos.y, startPos.z);
            while (t < 1f)
            {
                t += Time.deltaTime / jumpDuration;
                float y = Mathf.Sin(t * Mathf.PI) * jumpHeight;
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = basePos + Vector3.up * y;
                if (cc != null) cc.enabled = true;
                yield return null;
            }
            // Asegurar posición final
            CharacterController cc2 = player.GetComponent<CharacterController>();
            if (cc2 != null) cc2.enabled = false;
            player.transform.position = targetPos;
            if (cc2 != null) cc2.enabled = true;
        }
        else
        {
            // Movimiento lateral + salto parabólico hacia slot
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / jumpDuration;
                Vector3 horiz = Vector3.Lerp(startPos, targetPos, t);
                float y = Mathf.Sin(t * Mathf.PI) * jumpHeight;
                horiz.y += y;
                CharacterController cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = horiz;
                if (cc != null) cc.enabled = true;
                yield return null;
            }
            CharacterController cc3 = player.GetComponent<CharacterController>();
            if (cc3 != null) cc3.enabled = false;
            player.transform.position = targetPos;
            if (cc3 != null) cc3.enabled = true;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (podiumSlots == null) return;
        for (int i = 0; i < podiumSlots.Length; i++)
        {
            Transform t = podiumSlots[i];
            if (t == null) continue;
            Gizmos.color = i == 0 ? Color.yellow : (i == 1 ? Color.gray : new Color(0.8f, 0.5f, 0.2f));
            Gizmos.DrawWireCube(t.position, blockSize);
            Gizmos.DrawLine(t.position, t.position + Vector3.up * 3f);
        }
    }

    private void OnValidate()
    {
        if (blockHeight <= 0f) blockHeight = 0.5f;
        if (blockSpawnInterval <= 0f) blockSpawnInterval = 0.1f;
        if (fallbackSpacing <= 0f) fallbackSpacing = 3f;
        if (cameraFollowThresholdBlocks < 0) cameraFollowThresholdBlocks = 0;
    }
#endif
}
