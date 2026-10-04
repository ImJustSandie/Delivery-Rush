using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MarcadorEdificio
{
    /// <summary>
    /// Componente de marcador y estela visual para la zona de entrega de puntos.
    /// Crea dinámicamente una estela/anillo luminoso alrededor del Collider del edificio,
    /// con proyección vertical (pilar/columna de luz 3D) opcional, animación de respiración
    /// y destello al depositar puntos.
    /// Ubicación: Assets/Scripts/marcadorEdificio/DeliveryZoneMarker.cs
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DeliveryBuilding))]
    public class DeliveryZoneMarker : MonoBehaviour
    {
        [Header("Configuración de Color y Marcador")]
        [Tooltip("Color base de la estela / zona de entrega.")]
        [SerializeField] private Color baseColor = new Color(0f, 0.9f, 1f, 0.8f); // Cyan translúcido

        [Tooltip("Color del destello al depositar puntos.")]
        [SerializeField] private Color depositFlashColor = new Color(1f, 0.85f, 0.2f, 1f); // Dorado intenso

        [Tooltip("Ancho de la línea del perímetro.")]
        [SerializeField] private float lineWidth = 0.25f;

        [Header("Posición y Desplazamiento (Offsets)")]
        [Tooltip("Elevación ligera sobre el suelo para evitar Z-Fighting.")]
        [SerializeField] private float yOffset = 0.05f;

        [Tooltip("Desplazamiento adicional en el eje X.")]
        [SerializeField] private float xOffset = 0f;

        [Tooltip("Desplazamiento adicional en el eje Z.")]
        [SerializeField] private float zOffset = 0f;

        [Header("Proyección Vertical (Pilar de Luz / Altura)")]
        [Tooltip("Activa la proyección vertical hacia arriba formando una columna de luz 3D.")]
        [SerializeField] private bool enableVerticalProjection = true;

        [Tooltip("Altura de la proyección vertical hacia arriba (unidades).")]
        [SerializeField] private float projectionHeight = 2.0f;

        [Tooltip("Multiplicador de transparencia de la columna de luz interior (0 a 1).")]
        [Range(0.01f, 0.9f)]
        [SerializeField] private float volumeAlphaMultiplier = 0.25f;

        [Tooltip("Si es true, muestra también una estela/anillo perimetral en la parte superior del pilar.")]
        [SerializeField] private bool showTopRing = true;

        [Header("Dimensiones y Tamaño (Ancho / Largo)")]
        [Tooltip("Si es true, permite especificar el Ancho y Largo exactos manualmente. Si es false, se calcula desde el Collider del edificio.")]
        [SerializeField] private bool useCustomDimensions = false;

        [Tooltip("Ancho total de la zona en el eje X (se usa si useCustomDimensions es true).")]
        [SerializeField] private float customWidth = 3f;

        [Tooltip("Largo total de la zona en el eje Z (se usa si useCustomDimensions es true).")]
        [SerializeField] private float customLength = 3f;

        [Tooltip("Multiplicador de escala de Ancho (Eje X) para el cálculo automático.")]
        [SerializeField] private float widthScale = 1f;

        [Tooltip("Multiplicador de escala de Largo (Eje Z) para el cálculo automático.")]
        [SerializeField] private float lengthScale = 1f;

        [Tooltip("Margen/Padding extra al tamaño de la zona.")]
        [SerializeField] private float radiusMargin = 0.2f;

        [Header("Animación y Forma de la Estela")]
        [Tooltip("Velocidad de pulsación/respiración de la estela.")]
        [SerializeField] private float pulseSpeed = 2f;

        [Tooltip("Intensidad mínima de opacidad durante la pulsación (0 a 1).")]
        [Range(0.1f, 1f)]
        [SerializeField] private float minAlphaMultiplier = 0.5f;

        [Tooltip("Si es true, la forma será circular/ovalada (elipse). Si es false, se dibujará como rectángulo.")]
        [SerializeField] private bool forceCircularShape = true;

        [Tooltip("Número de segmentos para formas circulares u ovaladas.")]
        [Range(12, 120)]
        [SerializeField] private int circleSegments = 48;

        private LineRenderer lineRenderer;
        private LineRenderer topLineRenderer;
        private MeshFilter volumeMeshFilter;
        private MeshRenderer volumeMeshRenderer;
        private Mesh volumeMesh;

        private DeliveryBuilding deliveryBuilding;
        private Collider zoneCollider;

        private Material lineMaterial;
        private Material volumeMaterial;
        private Color currentAnimatedColor;
        private float flashTimer = 0f;
        private const float FlashDuration = 0.4f;

        private void Awake()
        {
            deliveryBuilding = GetComponent<DeliveryBuilding>();
            zoneCollider = GetComponent<Collider>();
            SetupRenderers();
        }

        private void OnEnable()
        {
            if (deliveryBuilding != null)
            {
                deliveryBuilding.OnDepositSuccessful += HandleDepositSuccessful;
            }
        }

        private void OnDisable()
        {
            if (deliveryBuilding != null)
            {
                deliveryBuilding.OnDepositSuccessful -= HandleDepositSuccessful;
            }
        }

        private void Start()
        {
            BuildMarkerGeometry();
        }

        private void Update()
        {
            AnimateMarker();
        }

        /// <summary>
        /// Inicializa y configura el LineRenderer y MeshRenderer del volumen 3D.
        /// </summary>
        private void SetupRenderers()
        {
            // Material Unlit/Sprites transparente
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply");

            lineMaterial = new Material(shader) { name = "DeliveryZoneMarker_LineMat" };
            volumeMaterial = new Material(shader) { name = "DeliveryZoneMarker_VolumeMat" };

            // 1. LineRenderer inferior
            lineRenderer = GetOrAddComponentChild<LineRenderer>("ZoneMarkerRenderer");
            ConfigureLineRenderer(lineRenderer);

            // 2. LineRenderer superior (opcional)
            topLineRenderer = GetOrAddComponentChild<LineRenderer>("ZoneMarkerTopRenderer");
            ConfigureLineRenderer(topLineRenderer);

            // 3. MeshRenderer de la columna de luz
            GameObject volumeObj = GetChildGameObject("ZoneVolumeRenderer");
            volumeMeshFilter = volumeObj.GetComponent<MeshFilter>();
            if (volumeMeshFilter == null) volumeMeshFilter = volumeObj.AddComponent<MeshFilter>();

            volumeMeshRenderer = volumeObj.GetComponent<MeshRenderer>();
            if (volumeMeshRenderer == null) volumeMeshRenderer = volumeObj.AddComponent<MeshRenderer>();

            volumeMeshRenderer.material = volumeMaterial;
            volumeMeshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            volumeMeshRenderer.receiveShadows = false;

            if (volumeMesh == null)
            {
                volumeMesh = new Mesh { name = "ZoneVolumeMesh" };
                volumeMeshFilter.sharedMesh = volumeMesh;
            }
        }

        private T GetOrAddComponentChild<T>(string childName) where T : Component
        {
            GameObject childObj = GetChildGameObject(childName);
            T comp = childObj.GetComponent<T>();
            if (comp == null) comp = childObj.AddComponent<T>();
            return comp;
        }

        private GameObject GetChildGameObject(string childName)
        {
            Transform child = transform.Find(childName);
            if (child != null) return child.gameObject;

            GameObject newChild = new GameObject(childName);
            newChild.transform.SetParent(transform, false);
            return newChild;
        }

        private void ConfigureLineRenderer(LineRenderer lr)
        {
            lr.material = lineMaterial;
            lr.useWorldSpace = false;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.loop = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
        }

        /// <summary>
        /// Genera los vértices del contorno del marcador y de la proyección vertical 3D.
        /// </summary>
        public void BuildMarkerGeometry()
        {
            if (lineRenderer == null) SetupRenderers();
            if (zoneCollider == null) zoneCollider = GetComponent<Collider>();

            Vector3 center = Vector3.zero;
            float radiusX = 1.5f;
            float radiusZ = 1.5f;

            if (useCustomDimensions)
            {
                radiusX = Mathf.Max(0.05f, customWidth * 0.5f);
                radiusZ = Mathf.Max(0.05f, customLength * 0.5f);
            }
            else
            {
                if (zoneCollider is SphereCollider sphere)
                {
                    center = sphere.center;
                    float maxScale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                    radiusX = radiusZ = (sphere.radius * maxScale) + radiusMargin;
                }
                else if (zoneCollider is BoxCollider box)
                {
                    center = box.center;
                    Vector3 size = box.size;
                    radiusX = (size.x * 0.5f) + radiusMargin;
                    radiusZ = (size.z * 0.5f) + radiusMargin;
                }
                else if (zoneCollider != null)
                {
                    Bounds b = zoneCollider.bounds;
                    center = transform.InverseTransformPoint(b.center);
                    radiusX = (b.extents.x) + radiusMargin;
                    radiusZ = (b.extents.z) + radiusMargin;
                }

                radiusX *= widthScale;
                radiusZ *= lengthScale;
            }

            // Aplicar desplazamientos (Offsets) en X, Y, Z
            center.x += xOffset;
            center.y += yOffset;
            center.z += zOffset;

            int count = forceCircularShape ? circleSegments : 4;
            Vector3[] bottomPositions = new Vector3[count];
            Vector3[] topPositions = new Vector3[count];

            float height = enableVerticalProjection ? Mathf.Max(0.1f, projectionHeight) : 0f;

            for (int i = 0; i < count; i++)
            {
                if (forceCircularShape)
                {
                    float angle = i * (2f * Mathf.PI / count);
                    float x = center.x + Mathf.Cos(angle) * radiusX;
                    float z = center.z + Mathf.Sin(angle) * radiusZ;
                    bottomPositions[i] = new Vector3(x, center.y, z);
                }
                else
                {
                    float signX = (i == 0 || i == 1) ? -1f : 1f;
                    float signZ = (i == 0 || i == 3) ? -1f : 1f;
                    bottomPositions[i] = new Vector3(center.x + signX * radiusX, center.y, center.z + signZ * radiusZ);
                }

                topPositions[i] = new Vector3(bottomPositions[i].x, bottomPositions[i].y + height, bottomPositions[i].z);
            }

            // Aplicar posiciones a LineRenderer inferior
            lineRenderer.positionCount = count;
            lineRenderer.SetPositions(bottomPositions);

            // Aplicar posiciones a LineRenderer superior
            bool activeTopRing = enableVerticalProjection && showTopRing && height > 0.05f;
            if (topLineRenderer != null)
            {
                topLineRenderer.gameObject.SetActive(activeTopRing);
                if (activeTopRing)
                {
                    topLineRenderer.positionCount = count;
                    topLineRenderer.SetPositions(topPositions);
                }
            }

            // Construir malla de la columna 3D de luz
            BuildVolumeMesh(bottomPositions, topPositions, count, activeTopRing);
        }

        private void BuildVolumeMesh(Vector3[] bottom, Vector3[] top, int count, bool activeVolume)
        {
            if (volumeMesh == null) return;

            if (!enableVerticalProjection || !activeVolume)
            {
                volumeMesh.Clear();
                if (volumeMeshRenderer != null) volumeMeshRenderer.enabled = false;
                return;
            }

            if (volumeMeshRenderer != null) volumeMeshRenderer.enabled = true;

            int vertCount = count * 2;
            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            List<int> triangles = new List<int>(count * 12); // Doble cara para visibilidad interior/exterior

            for (int i = 0; i < count; i++)
            {
                vertices[i] = bottom[i];
                vertices[i + count] = top[i];

                uvs[i] = new Vector2((float)i / count, 0f);
                uvs[i + count] = new Vector2((float)i / count, 1f);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;

                int bCurrent = i;
                int bNext = next;
                int tCurrent = i + count;
                int tNext = next + count;

                // Cara exterior
                triangles.Add(bCurrent);
                triangles.Add(tCurrent);
                triangles.Add(bNext);

                triangles.Add(bNext);
                triangles.Add(tCurrent);
                triangles.Add(tNext);

                // Cara interior
                triangles.Add(bCurrent);
                triangles.Add(bNext);
                triangles.Add(tCurrent);

                triangles.Add(bNext);
                triangles.Add(tNext);
                triangles.Add(tCurrent);
            }

            volumeMesh.Clear();
            volumeMesh.vertices = vertices;
            volumeMesh.uv = uvs;
            volumeMesh.triangles = triangles.ToArray();
            volumeMesh.RecalculateBounds();
            volumeMesh.RecalculateNormals();
        }

        /// <summary>
        /// Aplica la animación continua de estela (pulsación de opacidad y respuesta a entregas).
        /// </summary>
        private void AnimateMarker()
        {
            if (lineRenderer == null || lineMaterial == null) return;

            // Calcular factor de pulsación senoidal en base al tiempo
            float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f; // Rango 0 a 1
            float alphaFactor = Mathf.Lerp(minAlphaMultiplier, 1f, wave);

            Color targetColor = baseColor;

            // Manejo de flash por entrega
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(flashTimer / FlashDuration);
                targetColor = Color.Lerp(baseColor, depositFlashColor, t);
                alphaFactor = Mathf.Max(alphaFactor, t);
                lineRenderer.startWidth = lineWidth * (1f + t * 0.5f);
                if (topLineRenderer != null) topLineRenderer.startWidth = lineRenderer.startWidth;
            }
            else
            {
                lineRenderer.startWidth = lineWidth;
                if (topLineRenderer != null) topLineRenderer.startWidth = lineWidth;
            }

            if (topLineRenderer != null) topLineRenderer.endWidth = lineRenderer.startWidth;
            lineRenderer.endWidth = lineRenderer.startWidth;

            targetColor.a *= alphaFactor;
            currentAnimatedColor = targetColor;

            lineMaterial.color = currentAnimatedColor;
            lineRenderer.startColor = currentAnimatedColor;
            lineRenderer.endColor = currentAnimatedColor;

            if (topLineRenderer != null && topLineRenderer.gameObject.activeSelf)
            {
                topLineRenderer.startColor = currentAnimatedColor;
                topLineRenderer.endColor = currentAnimatedColor;
            }

            if (volumeMaterial != null && enableVerticalProjection)
            {
                Color volColor = currentAnimatedColor;
                volColor.a = currentAnimatedColor.a * volumeAlphaMultiplier;
                volumeMaterial.color = volColor;
            }
        }

        private void HandleDepositSuccessful(PlayerMovementManager player)
        {
            flashTimer = FlashDuration;
        }

        private void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
            if (volumeMaterial != null) Destroy(volumeMaterial);
            if (volumeMesh != null) Destroy(volumeMesh);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (lineWidth < 0.05f) lineWidth = 0.05f;
            if (circleSegments < 12) circleSegments = 12;
            if (customWidth < 0.1f) customWidth = 0.1f;
            if (customLength < 0.1f) customLength = 0.1f;
            if (widthScale < 0.05f) widthScale = 0.05f;
            if (lengthScale < 0.05f) lengthScale = 0.05f;
            if (projectionHeight < 0f) projectionHeight = 0f;

            if (lineRenderer != null)
            {
                lineRenderer.startWidth = lineWidth;
                lineRenderer.endWidth = lineWidth;
                BuildMarkerGeometry();
            }
        }
#endif
    }
}
