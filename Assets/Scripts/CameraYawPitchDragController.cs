using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using EnhancedTouch = UnityEngine.InputSystem.EnhancedTouch;

public enum DragScreenSide
{
    Left,
    Right,
    Full
}

public class CameraYawPitchDragController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
    [SerializeField] private float distance = 6f;

    [Header("Rotation")]
    [SerializeField] private float yawSensitivity = 0.25f;
    [SerializeField] private float pitchSensitivity = 0.25f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 60f;
    [SerializeField] private float startYaw;
    [SerializeField] private float startPitch = 15f;

    [Header("Drag Zone")]
    [SerializeField] private DragScreenSide dragSide = DragScreenSide.Left;
    [SerializeField] [Range(0.1f, 1f)] private float sideFraction = 0.5f;
    [SerializeField] private bool ignoreDragsOverUI = true;

    [Header("UI Filtering")]
    [Tooltip("Si se asignan, solo estos objetos (y cualquier Selectable / OnScreen control) bloquean el drag. Un fondo Image a pantalla completa ya no bloquea.")]
    [SerializeField] private GameObject virtualMovePadObject;
    [SerializeField] private GameObject interactButtonObject;

    [Header("Debug")]
    [SerializeField] private bool initializeFromCurrentTransform = true;

    private float yaw;
    private float pitch;
    private bool draggingMouse;
    private int activeTouchId = -1;

    private void OnEnable()
    {
        EnhancedTouch.EnhancedTouchSupport.Enable();
    }

    private void OnDisable()
    {
        EnhancedTouch.EnhancedTouchSupport.Disable();
        draggingMouse = false;
        activeTouchId = -1;
    }

    private void Awake()
    {
        yaw = startYaw;
        pitch = Mathf.Clamp(startPitch, minPitch, maxPitch);
    }

    private void Start()
    {
        if (initializeFromCurrentTransform)
        {
            Vector3 euler = transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }
    }

    private void Update()
    {
        HandleMouseDrag();
        HandleTouchDrag();
    }

    private void LateUpdate()
    {
        UpdateCameraTransform();
    }

    private void HandleMouseDrag()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 pos = mouse.position.ReadValue();
            bool inZone = IsInDragZone(pos);
            bool overUI = IsOverBlockingUI(pos);
            if (inZone && !overUI)
                draggingMouse = true;
        }

        if (draggingMouse && mouse.leftButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if (delta.sqrMagnitude > 0f)
            {
                ApplyDelta(delta);
            }
        }

        if (draggingMouse && mouse.leftButton.wasReleasedThisFrame)
            draggingMouse = false;
    }

    private void HandleTouchDrag()
    {
        foreach (EnhancedTouch.Touch touch in EnhancedTouch.Touch.activeTouches)
        {
            int touchId = touch.touchId;
            Vector2 pos = touch.screenPosition;
            UnityEngine.InputSystem.TouchPhase phase = touch.phase;

            if (phase == UnityEngine.InputSystem.TouchPhase.Began && activeTouchId == -1)
            {
                bool inZone = IsInDragZone(pos);
                bool overUI = IsOverBlockingUI(pos);
                if (inZone && !overUI)
                    activeTouchId = touchId;
            }
            else if (activeTouchId == touchId)
            {
                if (phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    Vector2 delta = touch.delta;
                    ApplyDelta(delta);
                }

                if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                    activeTouchId = -1;
            }
        }
    }

    private void ApplyDelta(Vector2 delta)
    {
        yaw += delta.x * yawSensitivity;
        pitch -= delta.y * pitchSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private bool IsInDragZone(Vector2 screenPos)
    {
        if (dragSide == DragScreenSide.Full) return true;

        float limit = Screen.width * sideFraction;
        if (dragSide == DragScreenSide.Left)
            return screenPos.x <= limit;
        return screenPos.x >= Screen.width - limit;
    }

    private bool IsOverBlockingUI(Vector2 screenPos)
    {
        if (!ignoreDragsOverUI) return false;
        if (EventSystem.current == null) return false;

        PointerEventData pointerData = new PointerEventData(EventSystem.current);
        pointerData.position = screenPos;
        System.Collections.Generic.List<RaycastResult> results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);
        if (results.Count == 0) return false;

        foreach (RaycastResult result in results)
        {
            GameObject hit = result.gameObject;
            if (hit == null) continue;
            if (IsInBlockedSubtree(hit)) return true;
            if (hit.GetComponentInParent<Selectable>() != null) return true;
            if (hit.GetComponentInParent<UnityEngine.InputSystem.OnScreen.OnScreenStick>() != null) return true;
            if (hit.GetComponentInParent<UnityEngine.InputSystem.OnScreen.OnScreenButton>() != null) return true;
            if (HasJoystickLikeComponent(hit)) return true;
        }

        // Solo habia fondos/decoracion (Image sin control): no bloquea.
        return false;
    }

    private bool IsInBlockedSubtree(GameObject hit)
    {
        if (virtualMovePadObject != null && IsChildOrSelf(virtualMovePadObject.transform, hit.transform)) return true;
        if (interactButtonObject != null && IsChildOrSelf(interactButtonObject.transform, hit.transform)) return true;
        return false;
    }

    private static bool IsChildOrSelf(Transform root, Transform candidate)
    {
        for (Transform t = candidate; t != null; t = t.parent)
        {
            if (t == root) return true;
        }
        return false;
    }

    private static bool HasJoystickLikeComponent(GameObject hit)
    {
        Component[] components = hit.GetComponentsInParent<Component>();
        foreach (Component component in components)
        {
            if (component == null) continue;
            string typeName = component.GetType().Name;
            if (typeName.IndexOf("joystick", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (typeName.IndexOf("pad", System.StringComparison.OrdinalIgnoreCase) >= 0
                && typeName.IndexOf("gamepad", System.StringComparison.OrdinalIgnoreCase) < 0) return true;
        }
        return false;
    }

    private void UpdateCameraTransform()
    {
        if (target == null)
        {
            return;
        }

        Camera controlledCamera = GetComponent<Camera>();
        Transform cameraTransform = controlledCamera != null ? controlledCamera.transform : transform;
        if (controlledCamera == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        Vector3 pivot = target.position + targetOffset;

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 desiredPosition = pivot + rotation * Vector3.back * distance;

        cameraTransform.position = desiredPosition;
        cameraTransform.rotation = rotation;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
