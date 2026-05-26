using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace ChronoLux.Interaction
{
    /// <summary>
    /// Snappy, precise camera controller updated for the Dashboard workflow.
    /// Click 3D world to lock cursor and fly. ESC to return to UI.
    /// </summary>
    public class FreeLookCamera : MonoBehaviour
    {
        public float sensitivity = 0.05f;
        public float speed = 5f;

        private Vector2 _rotation;

        private void Start()
        {
            _rotation.y = transform.eulerAngles.y;
            _rotation.x = transform.eulerAngles.x;
        }

        private void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            // 1. Handle Cursor Toggles
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            // Click to lock (if not clicking on a UI element)
            bool isOverUI = false;
            if (EventSystem.current != null)
            {
                isOverUI = EventSystem.current.IsPointerOverGameObject();
            }

            if (mouse.leftButton.wasPressedThisFrame && !isOverUI)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // 2. Navigation (Only when locked)
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 delta = mouse.delta.ReadValue() * sensitivity;
            _rotation.y += delta.x;
            _rotation.x = Mathf.Clamp(_rotation.x - delta.y, -89f, 89f);
            transform.localRotation = Quaternion.Euler(_rotation.x, _rotation.y, 0f);

            Vector3 input = Vector3.zero;
            var kb = Keyboard.current;
            if (kb.wKey.isPressed) input += transform.forward;
            if (kb.sKey.isPressed) input += -transform.forward;
            if (kb.aKey.isPressed) input += -transform.right;
            if (kb.dKey.isPressed) input += transform.right;
            if (kb.eKey.isPressed) input += Vector3.up;
            if (kb.qKey.isPressed) input += Vector3.down;

            float currentSpeed = speed;
            if (kb.shiftKey.isPressed) currentSpeed *= 10f;

            transform.position += input * currentSpeed * Time.deltaTime;
        }
    }
}
