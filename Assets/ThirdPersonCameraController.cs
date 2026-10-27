using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;

    [Header("Camera Settings")]
    [SerializeField] private float distance = 15f;
    [SerializeField] private float targetHeight = 1.5f;
    
    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 1f;
    [SerializeField] private float minDistance = 2f;
    [SerializeField] private float maxDistance = 30f;

    [Header("Mouse Settings")]
    [SerializeField] private float sensitivity = 0.15f;
    [SerializeField] private float startupPitch = 45f;
    [SerializeField] private float minPitch = -20f;
    [SerializeField] private float maxPitch = 65f;

    private float _yaw;
    private float _pitch;

    private void Start()
    {
        if (target == null)
        {
            Debug.LogError("ThirdPersonCameraController: Target is not assigned!");

            enabled = false;
            return;
        }

        ResetBehindTarget();
    }

    /// <summary>
    /// Resets the camera to a position behind the target using its current facing direction.
    /// Called both at startup and after the server applies the authoritative spawn rotation.
    /// </summary>
    public void ResetBehindTarget()
    {
        if (target == null)
        {
            return;
        }

        _yaw = target.eulerAngles.y;
        _pitch = Mathf.Clamp(startupPitch, minPitch, maxPitch);
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        // Получаем движение мыши только при зажатой ПКМ.
        Mouse mouse = Mouse.current;

        if (mouse != null)
        {
            // Вращение камеры только при зажатой правой кнопке.
            if (mouse.rightButton.isPressed)
            {
                Vector2 mouseDelta = mouse.delta.ReadValue();

                _yaw += mouseDelta.x * sensitivity;
                _pitch -= mouseDelta.y * sensitivity;

                _pitch = Mathf.Clamp(
                    _pitch,
                    minPitch,
                    maxPitch
                );
            }

            // Приближение и отдаление колёсиком мыши.
            float scroll = mouse.scroll.ReadValue().y;

            // На Windows один шаг колёсика обычно равен 120 единицам.
            if (Mathf.Abs(scroll) >= 10f)
            {
                scroll /= 120f;
            }

            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance = Mathf.Clamp(
                    distance - scroll * zoomSpeed,
                    minDistance,
                    maxDistance
                );
            }
        }

        // Точка, вокруг которой вращается камера.
        Vector3 pivot = target.position
                        + Vector3.up * targetHeight;

        // Рассчитываем положение камеры.
        Quaternion rotation = Quaternion.Euler(
            _pitch,
            _yaw,
            0f
        );

        Vector3 offset = rotation
                         * new Vector3(0f, 0f, -distance);

        transform.position = pivot + offset;

        // Камера всегда смотрит на персонажа.
        transform.rotation = Quaternion.LookRotation(
            pivot - transform.position,
            Vector3.up
        );
    }
}