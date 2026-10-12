using UnityEngine;

/// <summary>
/// Muestra/oculta un panel de UI desde botones (OnClick del Inspector).
/// Solo codigo: no toca escenas ni prefabs, solo activa/desactiva GameObjects.
/// Uso tipico: añadir al mismo GameObject del boton o a un objeto de la UI,
/// asignar targetPanel en el Inspector y conectar el OnClick del boton a
/// Show(), Hide() o Toggle(). Tambien hay variantes con parametro GameObject
/// para arrastrar el panel directamente como argumento del OnClick.
/// </summary>
public class UIPanelToggle : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Panel que se muestra/oculta cuando se usan Show()/Hide()/Toggle() sin parametros.")]
    [SerializeField] private GameObject targetPanel;

    [Header("Start")]
    [Tooltip("Si es true, el panel arranca oculto al cargar la escena.")]
    [SerializeField] private bool hideOnStart = true;

    private void Start()
    {
        if (hideOnStart && targetPanel != null && targetPanel.activeSelf)
        {
            targetPanel.SetActive(false);
        }
    }

    /// <summary>Muestra el panel asignado en targetPanel.</summary>
    public void Show()
    {
        ShowPanel(targetPanel);
    }

    /// <summary>Oculta el panel asignado en targetPanel.</summary>
    public void Hide()
    {
        HidePanel(targetPanel);
    }

    /// <summary>Alterna (muestra si estaba oculto y viceversa) el panel asignado.</summary>
    public void Toggle()
    {
        TogglePanel(targetPanel);
    }

    /// <summary>
    /// Muestra el panel indicado. Para OnClick con argumento:
    /// arrastra el GameObject del panel como parametro.
    /// </summary>
    public void ShowPanel(GameObject panel)
    {
        GameObject target = panel != null ? panel : targetPanel;
        if (target != null)
        {
            target.SetActive(true);
        }
    }

    /// <summary>
    /// Oculta el panel indicado. Para OnClick con argumento:
    /// arrastra el GameObject del panel como parametro.
    /// </summary>
    public void HidePanel(GameObject panel)
    {
        GameObject target = panel != null ? panel : targetPanel;
        if (target != null)
        {
            target.SetActive(false);
        }
    }

    /// <summary>
    /// Alterna el panel indicado. Ideal para un solo boton que abre/cierra
    /// (ej. boton de ajustes que muestra/oculta el panel de volumen).
    /// </summary>
    public void TogglePanel(GameObject panel)
    {
        GameObject target = panel != null ? panel : targetPanel;
        if (target != null)
        {
            target.SetActive(!target.activeSelf);
        }
    }
}
