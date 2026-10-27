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
    [SerializeField] private float forwardMarkerLength = 0.65f;
    [SerializeField] private float forwardMarkerRadius = 0.12f;
    [SerializeField] private Color forwardMarkerColor = new Color(1f, 0.25f, 0.05f);

    private Vector2 _move;
    private float _verticalVelocity;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
        }

        CreateForwardMarker();
    }

    private void CreateForwardMarker()
    {
        // The cylinder's local Y axis is rotated onto the character's local Z axis.
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

        // Slightly overlap the capsule so the marker looks attached to its front.
        marker.transform.localPosition = new Vector3(
            0f,
            controllerCenterY,
            controllerRadius + forwardMarkerLength * 0.5f - forwardMarkerRadius * 0.25f);

        // This is a visual marker only; it must not participate in collisions.
        Collider markerCollider = marker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
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

        // Keep the controller grounded without accumulating downward speed.
        if (characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;

        // Vector2.Y is mapped to world-space Z for forward/backward movement.
        Vector3 movement = new Vector3(_move.x, 0f, _move.y);
        movement = Vector3.ClampMagnitude(movement, 1f) * moveSpeed;
        movement.y = _verticalVelocity;

        characterController.Move(movement * Time.deltaTime);
    }
}
