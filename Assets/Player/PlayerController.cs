using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController: MonoBehaviour
{
    [SerializeField] private CharacterController characterController;
    [SerializeField] private CinemachineCamera cinemachineCamera;

    private Vector2 _move;
    public void OnMove(InputValue val)
    {
        _move = val.Get<Vector2>();
    }

    private void Update()
    {
        Vector3 movement = new Vector3(_move.x, 0f, _move.y);
        characterController.Move(movement * Time.deltaTime);
    }
}
