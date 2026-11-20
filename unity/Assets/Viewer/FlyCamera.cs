using UnityEngine;

namespace Stabby.Viewer
{
    public sealed class FlyCamera : MonoBehaviour
    {
        public float Speed = 300f;
        float yaw, pitch;

        void Start()
        {
            var e = transform.eulerAngles;
            yaw = e.y;
            pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        void Update()
        {
            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 3f, -89f, 89f);
            }
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            Speed = Mathf.Clamp(Speed * (1f + Input.mouseScrollDelta.y * 0.1f), 5f, 20000f);
            float s = Speed * (Input.GetKey(KeyCode.LeftShift) ? 10f : 1f) * Time.deltaTime;
            float up = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            var move = new Vector3(Input.GetAxisRaw("Horizontal"), up, Input.GetAxisRaw("Vertical"));
            transform.position += transform.rotation * move * s;
        }
    }
}
