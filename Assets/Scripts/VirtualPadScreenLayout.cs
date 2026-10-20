using UnityEngine;

/// <summary>
/// Normaliza el Container_VirtualPad para que ocupe un porcentaje fijo de la
/// pantalla (por defecto el 25%), anclado a la esquina inferior izquierda con
/// margen proporcional. Se reajusta automaticamente si cambia la resolucion
/// (rotacion del dispositivo, resize en editor, etc.).
///
/// Se auto-instala al entrar a escena: no hace falta agregarlo a mano en el Editor.
/// (Si ademas se agrega manualmente, el auto-attach lo ignora por duplicado.)
///
/// Con "Override Scene Layout" activo, cualquier tamaño/anchors hecho a mano en la
/// escena se normaliza en Play mode al cuadrado del 25%; para ajustar el tamaño use
/// las casillas de este componente. Si se desactiva, se respeta el layout de escena.
/// </summary>
public class VirtualPadScreenLayout : MonoBehaviour
{
    [Header("Tamaño")]
    [Tooltip("Si es true, el pad toma en runtime el tamaño/anclaje definido aqui, sin importar como este configurado en la escena.")]
    [SerializeField] private bool overrideSceneLayout = true;

    [Tooltip("Fraccion de pantalla que ocupa el lado del pad. 0.25 = su lado mide el 25% del lado mas corto de la pantalla.")]
    [SerializeField] [Range(0.1f, 1f)] private float screenFraction = 0.25f;

    [Tooltip("Margen entre el pad y el borde inferior-izquierdo, como fraccion del lado del pad (0.1 = 10%).")]
    [SerializeField] [Range(0f, 0.5f)] private float edgeMarginFraction = 0.1f;

    [Tooltip("Tamaño del stick interior, como fraccion del lado del pad (0.45 = el stick mide el 45% del pad).")]
    [SerializeField] [Range(0.1f, 1f)] private float stickFraction = 0.45f;

    private RectTransform padRect;
    private Canvas parentCanvas;
    private Vector2 lastScreenSize;

    /// <summary>
    /// Auto-instalacion: se engancha solo al Container_VirtualPad de cada escena de
    /// juego, sin que nadie agregue el componente a mano desde el Editor.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookSceneLoading()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= AttachToLoadedScene;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += AttachToLoadedScene;
        AttachToActiveScenePad();
    }

    private static void AttachToLoadedScene(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        AttachToActiveScenePad();
    }

    private static void AttachToActiveScenePad()
    {
        if (!GameplaySceneUtility.IsActiveSceneGameplay()) return;
        GameObject padContainer = GameObject.Find("Container_VirtualPad");
        if (padContainer == null) return;
        if (padContainer.GetComponent<VirtualPadScreenLayout>() != null) return;
        padContainer.AddComponent<VirtualPadScreenLayout>();
    }

    private void Awake()
    {
        // Fuera de la escena de juego toda la parte de UI tactil queda apagada.
        if (!GameplaySceneUtility.IsActiveSceneGameplay())
        {
            gameObject.SetActive(false);
            return;
        }

        padRect = GetComponent<RectTransform>();
        parentCanvas = GetComponentInParent<Canvas>();
        lastScreenSize = new Vector2(Screen.width, Screen.height);
        ApplyLayout();
    }

    private void Update()
    {
        Vector2 currentScreen = new Vector2(Screen.width, Screen.height);
        if (currentScreen != lastScreenSize)
        {
            lastScreenSize = currentScreen;
            ApplyLayout();
        }
    }

    private void ApplyLayout()
    {
        if (padRect == null) return;

        if (!overrideSceneLayout) return;

        // Anclar a la esquina inferior izquierda con tamano absoluto (cuadrado),
        // para que el resultado no dependa de como se configuro la escena
        // (anchors fijos o estirados da lo mismo: se reescriben aqui).
        padRect.pivot = new Vector2(0.5f, 0.5f);
        padRect.anchorMin = Vector2.zero;
        padRect.anchorMax = Vector2.zero;
        padRect.localScale = Vector3.one;

        // El pad es cuadrado: su lado = fraccion del lado mas corto de la pantalla real.
        float shortestScreenSide = Mathf.Min(Screen.width, Screen.height);
        float targetSidePixels = shortestScreenSide * screenFraction;

        // Convertir a unidades del canvas (el pad vive dentro de un Canvas con CanvasScaler).
        float canvasScale = parentCanvas != null ? parentCanvas.scaleFactor : 1f;
        float targetSideCanvas = targetSidePixels / Mathf.Max(canvasScale, 0.0001f);

        padRect.sizeDelta = new Vector2(targetSideCanvas, targetSideCanvas);

        // Recolocar el centro respecto al punto de anclaje (esquina inferior izquierda).
        float margin = targetSideCanvas * edgeMarginFraction;
        float halfSide = targetSideCanvas * 0.5f;
        padRect.anchoredPosition = new Vector2(margin + halfSide, margin + halfSide);

        // Centrar el stick interior (solo hijos directos) y darle tamano proporcional
        // al pad, porque con anchors/tamaños distintos de escena quedaria descentrado.
        float stickSizeCanvas = targetSideCanvas * stickFraction;
        for (int i = 0; i < padRect.transform.childCount; i++)
        {
            RectTransform child = padRect.transform.GetChild(i) as RectTransform;
            if (child == null) continue;
            child.anchorMin = new Vector2(0.5f, 0.5f);
            child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.localScale = Vector3.one;
            child.sizeDelta = new Vector2(stickSizeCanvas, stickSizeCanvas);
            child.anchoredPosition = Vector2.zero;
        }
    }
}
