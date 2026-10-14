using UnityEngine;

/// <summary>
/// Continuously rotates a GameObject around its Y axis at a configurable speed.
/// Useful for spinning prefabs such as collectibles, indicators, trophies, or showcase items.
/// </summary>
public class ConstantRotator : MonoBehaviour
{
    [Header("Rotation Settings")]
    [Tooltip("Rotation speed in degrees per second around the Y axis. Positive values rotate clockwise, negative counter-clockwise.")]
    [SerializeField] private float rotationSpeed = 20f;

    [Tooltip("Space in which the rotation is applied (Self uses the object's local up vector, World uses global up).")]
    [SerializeField] private Space rotationSpace = Space.Self;

    [Tooltip("If true, rotation is active. Can be toggled at runtime or from other scripts.")]
    [SerializeField] private bool isRotating = true;

    [Header("Time Settings")]
    [Tooltip("If true, uses unscaled time so the rotation continues even when the game is paused (Time.timeScale == 0).")]
    [SerializeField] private bool useUnscaledTime = false;

    /// <summary>
    /// Gets or sets the rotation speed in degrees per second.
    /// </summary>
    public float RotationSpeed
    {
        get => rotationSpeed;
        set => rotationSpeed = value;
    }

    /// <summary>
    /// Gets or sets whether rotation is currently active.
    /// </summary>
    public bool IsRotating
    {
        get => isRotating;
        set => isRotating = value;
    }

    private void Update()
    {
        if (!isRotating) return;

        float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        transform.Rotate(Vector3.up, rotationSpeed * delta, rotationSpace);
    }
}
