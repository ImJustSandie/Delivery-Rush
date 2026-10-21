#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Herramienta de editor para dejar TutorialScene con TODO el contenido funcional
/// de la escena de gameplay (MainScene) sin el componente online.
///
/// Qué hace (repetible, idempotente mientras el Canvas de gameplay no cambie):
/// 1. Abre TutorialScene y MainScene a la vez.
/// 2. Copia el HUD completo (raíz "Canvas" y toda su jerarquía) de MainScene al
///    tutorial: dock de puntaje, Timer, ComidaDorada/Festín, Img_freeze,
///    VirtualPad con layout, panel de countdown, etc.
/// 3. Elimina la HUD antigua del tutorial.
/// 4. Sincroniza los valores de CollectibleSpawnManager del ItemManager con los
///    de gameplay (config de objetos dorados incluida) y re-vincula las imágenes
///    doradas a las de la HUD copiada.
/// 5. Agrega TutorialMatchTimer (timer visual de 2 minutos, offline) al Canvas.
/// 6. Guarda TutorialScene y cierra MainScene sin guardar cambios.
///
/// Mantener: TutorialInfoCanvas, Tutorial Manager (TutorialSceneSetup), dummy,
/// SpawnPoints/Luces (posiciones afinadas del tutorial) y prefabs de escenario.
/// </summary>
public static class TutorialSyncTool
{
    private const string TutorialScenePath = "Assets/Scenes/TutorialScene.unity";
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string CanvasName = "Canvas";
    private const string GoldenNotifyImageName = "ComidaDorada";
    private const string GoldenEventImageName = "Fest"; // el nombre real es "Festín"

    private static readonly string[] NonCopyablePropNames =
    {
        "m_GameObject", "m_Script", "m_Name", "m_EditorClassIdentifier", "m_ObjectHideFlags",
        "m_EditorHideFlags", "m_PrefabInstance", "m_PrefabAsset", "m_CorrespondingSourceObject",
        "m_PrefabParentObject", "m_PrefabInstanceGroup",
    };

    [MenuItem("Tools/Tutorial/Sincronizar Tutorial con Gameplay")]
    public static void SyncTutorialWithGameplay()
    {
        if (!EditorUtility.DisplayDialog(
                "Sincronizar Tutorial",
                "Se copiará el HUD completo de MainScene a TutorialScene (sin componentes online), " +
                "se sincronizará el ItemManager con la config de gameplay y se agregará el timer visual de 2 minutos.\n\n" +
                "Se guardarán los cambios en TutorialScene. ¿Continuar?",
                "Sincronizar", "Cancelar"))
        {
            return;
        }

        // 1) Asegurar TutorialScene abierta y activa
        Scene tutorialScene;
        Scene currentActive = EditorSceneManager.GetActiveScene();
        if (currentActive.path == TutorialScenePath)
        {
            tutorialScene = currentActive;
        }
        else
        {
            EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
            tutorialScene = EditorSceneManager.OpenScene(TutorialScenePath, OpenSceneMode.Single);
            if (tutorialScene.path != TutorialScenePath)
            {
                Debug.LogError("[TutorialSyncTool] No se pudo abrir TutorialScene. Abortando.");
                return;
            }
        }

        // 2) Abrir MainScene en aditivo
        Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
        if (!mainScene.IsValid() || mainScene.path != MainScenePath)
        {
            Debug.LogError("[TutorialSyncTool] No se pudo abrir MainScene aditivamente. Abortando.");
            return;
        }
        SceneManager.SetActiveScene(tutorialScene);

        GameObject mainCanvas = FindRootInScene(mainScene, CanvasName);
        GameObject oldCanvas = FindRootInScene(tutorialScene, CanvasName);
        GameObject itemManager = FindRootInScene(tutorialScene, "ItemManager");

        if (mainCanvas == null)
        {
            Debug.LogError("[TutorialSyncTool] No se encontró el Canvas de MainScene. Abortando.");
            EditorSceneManager.CloseScene(mainScene, true);
            return;
        }
        if (itemManager == null)
        {
            Debug.LogError("[TutorialSyncTool] No se encontró ItemManager en TutorialScene. Abortando.");
            EditorSceneManager.CloseScene(mainScene, true);
            return;
        }

        // 3) Copiar el HUD completo de gameplay al tutorial
        GameObject canvasCopy = Object.Instantiate(mainCanvas, null);
        canvasCopy.name = CanvasName + " (Gameplay Copy)";
        int copiedObjects = canvasCopy.GetComponentsInChildren<Transform>(true).Length;
        Debug.Log($"[TutorialSyncTool] HUD de MainScene copiada al tutorial ({copiedObjects} objetos).");

        // 4) Sincronizar CollectibleSpawnManager (ItemManager) con la config de gameplay
        CollectibleSpawnManager tutorialSpawnManager = itemManager.GetComponentInChildren<CollectibleSpawnManager>(true);
        CollectibleSpawnManager mainSpawnManager = FindRootInScene(mainScene, "ItemManager")?.GetComponentInChildren<CollectibleSpawnManager>(true);
        if (tutorialSpawnManager != null && mainSpawnManager != null)
        {
            CopyComponentValuesWithoutSceneRefs(mainSpawnManager, tutorialSpawnManager);

            // Re-vincular las imágenes doradas a las de la HUD copiada (campos private → SerializedObject)
            SerializedObject so = new SerializedObject(tutorialSpawnManager);
            so.FindProperty("goldenNotificationImage").objectReferenceValue = FindImageInCanvas(canvasCopy, GoldenNotifyImageName);
            so.FindProperty("goldenEventImage").objectReferenceValue = FindImageInCanvas(canvasCopy, GoldenEventImageName);
            so.ApplyModifiedPropertiesWithoutUndo();

            Image notifyImage = FindImageInCanvas(canvasCopy, GoldenNotifyImageName);
            Image eventImage = FindImageInCanvas(canvasCopy, GoldenEventImageName);
            Debug.Log($"[TutorialSyncTool] ItemManager sincronizado con gameplay. " +
                      $"goldenNotificationImage={(notifyImage ? notifyImage.name : "NULL")}, " +
                      $"goldenEventImage={(eventImage ? eventImage.name : "NULL")}");
        }

        // 5) Eliminar la HUD antigua del tutorial y renombrar la copia
        if (oldCanvas != null)
        {
            Object.DestroyImmediate(oldCanvas);
            Debug.Log("[TutorialSyncTool] HUD antigua del tutorial eliminada.");
        }
        canvasCopy.name = CanvasName;

        // 6) Timer visual de 2 minutos (offline, solo visual)
        var existingTimer = canvasCopy.GetComponent<TutorialMatchTimer>();
        if (existingTimer == null)
        {
            TutorialMatchTimer timer = canvasCopy.AddComponent<TutorialMatchTimer>();
            EditorUtility.SetDirty(timer);
            Debug.Log("[TutorialSyncTool] TutorialMatchTimer (2 min, solo visual) agregado al Canvas del tutorial.");
        }

        // 7) Auditoría de referencias: nada de la copia debe apuntar a objetos de la escena MainScene
        AuditSceneReferences(canvasCopy);

        // 8) Guardar TutorialScene y cerrar MainScene sin guardarla
        EditorSceneManager.MarkSceneDirty(tutorialScene);
        bool saved = EditorSceneManager.SaveScene(tutorialScene, TutorialScenePath);
        Debug.Log($"[TutorialSyncTool] TutorialScene guardada: {saved}.");
        EditorSceneManager.CloseScene(mainScene, true);
        Debug.Log("[TutorialSyncTool] ✓ Sincronización completa." +
                  "\n- Sin ExpressTutorial ni cuenta 3-2-1 (el juego arranca libre)." +
                  "\n- Timer visual de 2 minutos; al llegar a 00:00 no pasa nada." +
                  "\n- Todo lo demás del gameplay (recogida, entrega, power-ups, congelado, evento dorado) corre offline.");
    }

    private static GameObject FindRootInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name) return root;
        }
        return null;
    }

    private static Image FindImageInCanvas(GameObject canvas, string name)
    {
        foreach (var image in canvas.GetComponentsInChildren<Image>(true))
        {
            if (image.name == name) return image;
            if (image.name.Contains(name)) return image;
        }
        return null;
    }

    /// <summary>
    /// Copia los valores serializados de un componente a otro del MISMO tipo,
    /// saltando referencias a objetos de escena (se re-vinculan a mano) y las
    /// metadadas de Unity. Los assets (materiales, prefabs, sprites) sí se copian.
    /// </summary>
    private static void CopyComponentValuesWithoutSceneRefs(UnityEngine.Object source, UnityEngine.Object dest)
    {
        SerializedObject srcSo = new SerializedObject(source);
        SerializedObject dstSo = new SerializedObject(dest);

        var it = srcSo.GetIterator();
        it.Next(true);
        while (it.Next(true))
        {
            // Solo campos de primer nivel (CopyFromSerializedProperty copia el bloque
            // entero de cada campo, incluidos structs y arrays).
            if (it.depth != 0) continue;
            if (NonCopyablePropNames.Contains(it.name)) continue;

            SerializedProperty dstProp = dstSo.FindProperty(it.name);
            if (dstProp == null) continue;
            if (dstProp.propertyType != it.propertyType) continue;

            switch (it.propertyType)
            {
                case SerializedPropertyType.ObjectReference:
                    // Copiar solo assets persistentes (materiales, prefabs, sprites).
                    // Las referencias a objetos de escena de MainScene se re-vinculan después.
                    var obj = it.objectReferenceValue;
                    if (obj == null || EditorUtility.IsPersistent(obj))
                    {
                        dstProp.objectReferenceValue = obj;
                    }
                    break;
                default:
                    dstSo.CopyFromSerializedProperty(it);
                    break;
            }
        }
        dstSo.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Revisa todos los componentes de la copia y avisa si alguna referencia
    /// apunta a objetos que NO son parte de la copia ni assets persistentes
    /// (es decir, referencias colgadas que se deben revisar).
    /// </summary>
    private static void AuditSceneReferences(GameObject root)
    {
        var ownedTransforms = new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
        var ownedObjects = new HashSet<Object>();
        foreach (var t in ownedTransforms)
        {
            ownedObjects.Add(t.gameObject);
            foreach (var comp in t.GetComponents<Component>()) ownedObjects.Add(comp);
        }

        int problems = 0;
        foreach (var t in ownedTransforms)
        {
            foreach (var comp in t.GetComponents<Component>())
            {
                var so = new SerializedObject(comp);
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = it.objectReferenceValue;
                    if (value == null) continue;
                    if (EditorUtility.IsPersistent(value)) continue;
                    if (ownedObjects.Contains(value)) continue; // ref interna a la copia

                    problems++;
                    Debug.LogWarning($"[TutorialSyncTool] Referencia colgada en {comp.name}.{it.name}: {value} (objeto no persistente ajeno a la copia).");
                }
            }
        }

        if (problems == 0)
            Debug.Log("[TutorialSyncTool] Auditoría OK: ninguna referencia del HUD copiado apunta fuera de sí misma.");
        else
            Debug.LogError($"[TutorialSyncTool] Auditoría con {problems} referencias posiblemente rotas. Revisar consola.");
    }
}
#endif
