using UnityEngine;
using UnityEngine.InputSystem;

public class SimpleFreeCameraInputSystem : MonoBehaviour
{
    public float moveSpeed = 100f;
    public float lookSensitivity = 0.1f;

    Vector2 moveInput;
    Vector2 lookInput;
    bool upPressed;
    bool downPressed;
    bool escapePressed;

    float yaw;
    float pitch;

    public void OnMove(InputAction.CallbackContext context) => moveInput = context.ReadValue<Vector2>();
    public void OnLook(InputAction.CallbackContext context) => lookInput = context.ReadValue<Vector2>();
    public void OnUp(InputAction.CallbackContext context) => upPressed = context.ReadValueAsButton();
    public void OnDown(InputAction.CallbackContext context) => downPressed = context.ReadValueAsButton();

    public void OnEscape(InputAction.CallbackContext context) => escapePressed = context.ReadValueAsButton();

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {

        if (Cursor.visible == true)
            Cursor.lockState = CursorLockMode.None;
        else
            Cursor.lockState = CursorLockMode.Locked;


        if (Cursor.lockState == CursorLockMode.None)
            return;

        // Rotation
        yaw += lookInput.x * lookSensitivity;
        pitch -= lookInput.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -90f, 90f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Déplacement (ZQSD)
        Vector3 move = new Vector3(moveInput.x, 0f, moveInput.y);
        move = transform.TransformDirection(move);

        if (upPressed) move += transform.up;
        if (downPressed) move -= transform.up;

        transform.position += move * moveSpeed * Time.deltaTime;
    }
}
