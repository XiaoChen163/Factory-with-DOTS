using UnityEngine;

namespace FactoryWithDots.Stage0.Input
{
    public sealed class TopDownPlayerController : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 7f;
        [SerializeField, Min(1f)] private float yawSensitivity = 110f;
        [SerializeField] private Vector2 xBounds = new Vector2(-4f, 20f);
        [SerializeField] private Vector2 zBounds = new Vector2(-8f, 18f);

        private void Update()
        {
            MoveParallelToGrid();
            RotateAroundWorldUp();
        }

        private void MoveParallelToGrid()
        {
            Vector3 planarForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 planarRight = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
            Vector3 input = planarRight * UnityEngine.Input.GetAxisRaw("Horizontal") +
                            planarForward * UnityEngine.Input.GetAxisRaw("Vertical");

            if (input.sqrMagnitude > 1f)
            {
                input.Normalize();
            }

            Vector3 nextPosition = transform.position + input * (moveSpeed * Time.deltaTime);
            nextPosition.x = Mathf.Clamp(nextPosition.x, xBounds.x, xBounds.y);
            nextPosition.z = Mathf.Clamp(nextPosition.z, zBounds.x, zBounds.y);
            transform.position = nextPosition;
        }

        private void RotateAroundWorldUp()
        {
            if (!UnityEngine.Input.GetMouseButton(1))
            {
                return;
            }

            float yaw = UnityEngine.Input.GetAxis("Mouse X") * yawSensitivity * Time.deltaTime;
            transform.Rotate(Vector3.up, yaw, Space.World);
        }
    }
}
