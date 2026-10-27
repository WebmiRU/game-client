using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController: MonoBehaviour
{
    [SerializedField] private CharacterController characterController;
    [SerializedField] private CinemachineCamera cinemachineCamera;

    public void OnMove(InputValue val)
    {
        Debug.Log("Move 101");
    }
}
