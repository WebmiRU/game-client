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

    private Vector2 _move;
    private float _verticalVelocity;

    private void Awake()
    {
        if (characterController == null)
        {
            characterController = GetComponent<CharacterController>();
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
