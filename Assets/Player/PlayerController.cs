using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -9.81f;
    [SerializeField] private float rotationSpeed = 720f;
    [SerializeField] private float forwardMarkerLength = 0.65f;
    [SerializeField] private float forwardMarkerRadius = 0.12f;
    [SerializeField] private Color forwardMarkerColor = new Color(1f, 0.25f, 0.05f);
    [SerializeField] private float clickStopDistance = 0.2f;

    private Camera playerCamera;
    private Vector2 _move;
    private float _verticalVelocity;
    private Vector3 _targetPosition;
    private bool _hasMoveTarget;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        // CinemachineCamera is virtual. Camera.main is the real camera that renders the view.
        playerCamera = Camera.main;
        if (playerCamera == null)
        {
            playerCamera = FindFirstObjectByType<Camera>();
        }

        if (playerCamera == null)
        {
            Debug.LogError("PlayerController: no rendering Camera found. Ensure the scene has a Camera (normally tagged MainCamera).");
        }

        CreateForwardMarker();
    }

    private void CreateForwardMarker()
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name = "ForwardMarker";
        marker.transform.SetParent(transform, false);
        marker.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        marker.transform.localScale = new Vector3(
            forwardMarkerRadius * 2f,
            forwardMarkerLength * 0.5f,
            forwardMarkerRadius * 2f);

        float controllerRadius = characterController != null
            ? characterController.radius
            : 0.5f;
        float controllerCenterY = characterController != null
            ? characterController.center.y
            : 1f;

        marker.transform.localPosition = new Vector3(
            0f,
            controllerCenterY,
            controllerRadius + forwardMarkerLength * 0.5f - forwardMarkerRadius * 0.25f);

        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Destroy(markerCollider);
        }

        Renderer markerRenderer = marker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            markerRenderer.material.color = forwardMarkerColor;
        }
    }

    public void OnMove(InputValue val)
    {
        _move = val.Get<Vector2>();
    }

    private void Update()
    {
        if (characterController == null)
        {
            return;
        }

        // Clicking the world selects a Terrain point to walk to.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TrySetMoveTargetFromMouse();
        }

        // Manual movement takes priority over click-to-move.
        Vector3 horizontalMovement = new Vector3(_move.x, 0f, _move.y);
        horizontalMovement = Vector3.ClampMagnitude(horizontalMovement, 1f);

        if (horizontalMovement.sqrMagnitude > 0.001f)
        {
            _hasMoveTarget = false;
        }
        else if (_hasMoveTarget)
        {
            Vector3 toTarget = _targetPosition - transform.position;
            toTarget.y = 0f;

            if (toTarget.magnitude <= clickStopDistance)
            {
                _hasMoveTarget = false;
            }
            else
            {
                horizontalMovement = toTarget.normalized;
            }
        }

        if (characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;

        if (horizontalMovement.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(horizontalMovement, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime);
        }

        Vector3 movement = horizontalMovement * moveSpeed;
        movement.y = _verticalVelocity;

        characterController.Move(movement * Time.deltaTime);
    }

    private void TrySetMoveTargetFromMouse()
    {
        // Resolve again in case the camera became active after this component's Awake.
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (playerCamera == null)
        {
            Debug.LogError("Click-to-move: no rendering Camera found; ray was not created.");
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        Ray ray = playerCamera.ScreenPointToRay(mousePosition);

        // Search all hits and select the nearest TerrainCollider specifically.
        // This avoids accidentally treating the character or another object as terrain.
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            1000f,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        bool foundTerrain = false;
        RaycastHit terrainHit = default;

        foreach (RaycastHit hit in hits)
        {
            if (hit.collider is TerrainCollider &&
                (!foundTerrain || hit.distance < terrainHit.distance))
            {
                terrainHit = hit;
                foundTerrain = true;
            }
        }

        if (!foundTerrain)
        {
            if (hits.Length > 0)
            {
                Debug.LogWarning(
                    $"Click-to-move: ray hit '{hits[0].collider.name}', but no TerrainCollider was found along the ray.");
            }
            else
            {
                Debug.LogWarning("Click-to-move: ray did not hit any Collider. Check that the Terrain has an enabled TerrainCollider.");
            }

            return;
        }

        _targetPosition = terrainHit.point;
        _hasMoveTarget = true;

        Debug.Log($"Click-to-move target: {_targetPosition} (Terrain: {terrainHit.collider.name})");
    }
}
