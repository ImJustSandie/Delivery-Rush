using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Define que escenas cuentan como "escena de juego" (donde vive toda la parte
/// tactil: arrastre de camara y virtual pad). Fuera de ellas los componentes de
/// input/UI tactil se desactivan por completo.
/// </summary>
public static class GameplaySceneUtility
{
    public const string GameSceneName = "MainScene";
    public const string TutorialSceneName = "TutorialScene";

    public static bool IsGameplayScene(string sceneName)
    {
        return sceneName == GameSceneName || sceneName == TutorialSceneName;
    }

    public static bool IsActiveSceneGameplay()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.IsValid() && IsGameplayScene(scene.name);
    }
}
