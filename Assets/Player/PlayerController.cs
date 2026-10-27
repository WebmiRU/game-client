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
    [SerializeField] private CinemachineBrain cinemachineBrain;

    private Camera playerCamera;
    private Vector2 _move;
    private float _verticalVelocity;
    private Vector2 _mousePosition;

    private void Awake()
    {
        if (cinemachineBrain == null)
        {
            cinemachineBrain = FindFirstObjectByType<CinemachineBrain>();
        }

        if (cinemachineBrain != null)
        {
            playerCamera = cinemachineBrain.OutputCamera;
        }
        else
        {
            Debug.LogError("CinemachineBrain не найден! Проверь, что он установлен на реальной камере, выводящей изображение.");
        }

        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
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

        if (characterController.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f;
        }

        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 horizontalMovement = new Vector3(_move.x, 0f, _move.y);
        horizontalMovement = Vector3.ClampMagnitude(horizontalMovement, 1f);

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
}
