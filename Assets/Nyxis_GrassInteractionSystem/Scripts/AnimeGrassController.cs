using UnityEngine;
using UnityEngine.InputSystem;

namespace Nyxis.GrassInteraction
{
    [RequireComponent(typeof(CharacterController))]
    public class AnimeGrassController : MonoBehaviour
    {
        [Header("--- MOVEMENT SETTINGS ---")]
        public float walkSpeed = 5.0f;
        public float runSpeed = 10.0f;
        public float rotationSpeed = 12.0f;

        [Header("--- JUMP & GRAVITY ---")]
        public float jumpHeight = 2.0f;
        public float gravity = -25.0f;
        public float groundCheckOffset = 0.2f;
        public LayerMask groundLayer;

        [Header("--- PLAYER HEIGHT (CharacterController) ---")]
        public float playerHeight = 2.0f;

        [Header("--- INTERACTION (Shader) ---")]
        public float grassPushRadius = 2.0f;
        [Tooltip("Zyada value = grass push zyada smooth/lagged, kam = instant snap")]
        public float grassPushSmoothTime = 0.08f;

        [Header("--- CAMERA SETTINGS ---")]
        public Transform cameraPivot;
        public float mouseSensitivity = 0.1f;
        public float distanceFromPlayer = 5.0f;
        public float minZoom = 1.5f;
        public float maxZoom = 10f;
        public float zoomSensitivity = 0.15f;
        public Vector2 pitchMinMax = new Vector2(-40, 85);
        public float camSmoothTime = 0.12f;

        [Header("--- OVER-THE-SHOULDER OFFSET ---")]
        [Tooltip("Positive = camera right side pe, player grass ko block nahi karega")]
        public float shoulderOffset = 0.8f;
        public float heightOffset = 0.3f;

        [Header("--- CAMERA COLLISION ---")]
        public LayerMask cameraCollisionMask = ~0;
        public float cameraCollisionRadius = 0.3f;
        public float cameraCollisionBuffer = 0.2f;


        private CharacterController _cc;
        private Transform _camTransform;
        private Vector3 _velocity;
        private float _currentSpeed;
        private float _yaw, _pitch;
        private float _yawVelocity, _pitchVelocity;
        private bool _isGrounded;
        private bool _skipNextMouseDelta;
        private const float MAX_DELTA_PER_FRAME = 50f;
        private float _currentDistance;
        private Vector3 _smoothedPushPos;
        private Vector3 _pushPosVelocity;

        void Start()
        {
            _cc = GetComponent<CharacterController>();

            if (!cameraPivot) cameraPivot = transform;

            if (Camera.main != null)
            {
                _camTransform = Camera.main.transform;
            }
            else
            {
                Debug.LogError("Main Camera nahi mila scene mein!");
            }

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            _skipNextMouseDelta = true;

            _yaw = transform.eulerAngles.y;
            _currentDistance = distanceFromPlayer;
            _smoothedPushPos = transform.position;


            _cc.height = playerHeight;
            _cc.center = new Vector3(0, playerHeight / 2f, 0);
        }

        void Update()
        {
            HandleCursorToggle();


            _isGrounded = Physics.CheckSphere(transform.position + Vector3.up * groundCheckOffset, 0.2f, groundLayer, QueryTriggerInteraction.Ignore);

            if (_isGrounded && _velocity.y < 0)
            {
                _velocity.y = -2f;
            }


            Vector2 moveInput = ReadMoveInput();
            bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            Vector2 inputDir = moveInput.normalized;
            Vector3 horizontalMove = Vector3.zero;

            if (inputDir.magnitude >= 0.1f)
            {
                float targetAngle = Mathf.Atan2(inputDir.x, inputDir.y) * Mathf.Rad2Deg + _camTransform.eulerAngles.y;

                float angle = Mathf.LerpAngle(transform.eulerAngles.y, targetAngle, rotationSpeed * Time.deltaTime);
                transform.rotation = Quaternion.Euler(0, angle, 0);

                Vector3 moveDir = Quaternion.Euler(0, targetAngle, 0) * Vector3.forward;

                _currentSpeed = isSprinting ? runSpeed : walkSpeed;

                horizontalMove = moveDir.normalized * _currentSpeed;
            }
            else
            {
                _currentSpeed = 0;
            }

            bool jumpPressed = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                             || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

            if (jumpPressed && _isGrounded)
            {
                _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }


            _velocity.y += gravity * Time.deltaTime;

            Vector3 finalMove = (horizontalMove + new Vector3(0, _velocity.y, 0)) * Time.deltaTime;
            _cc.Move(finalMove);


            HandleZoom();


            _smoothedPushPos = Vector3.SmoothDamp(_smoothedPushPos, transform.position, ref _pushPosVelocity, grassPushSmoothTime);
            Shader.SetGlobalVector("_GlobalPlayerPos", _smoothedPushPos);
            Shader.SetGlobalFloat("_GlobalPlayerRadius", grassPushRadius);
        }

        private void HandleZoom()
        {
            if (Mouse.current == null) return;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _currentDistance = Mathf.Clamp(_currentDistance + scroll * zoomSensitivity, minZoom, maxZoom);
            }
        }

        void LateUpdate()
        {
            if (!_camTransform) return;


            Vector2 lookDelta;

            if (_skipNextMouseDelta)
            {
                lookDelta = Vector2.zero;
                _skipNextMouseDelta = false;
            }
            else
            {
                lookDelta = ReadLookInput();
                lookDelta.x = Mathf.Clamp(lookDelta.x, -MAX_DELTA_PER_FRAME, MAX_DELTA_PER_FRAME);
                lookDelta.y = Mathf.Clamp(lookDelta.y, -MAX_DELTA_PER_FRAME, MAX_DELTA_PER_FRAME);
            }

            float targetYaw = _yaw + lookDelta.x * mouseSensitivity;
            float targetPitch = _pitch - lookDelta.y * mouseSensitivity;
            targetPitch = Mathf.Clamp(targetPitch, pitchMinMax.x, pitchMinMax.y);

            _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, camSmoothTime);
            _pitch = Mathf.SmoothDampAngle(_pitch, targetPitch, ref _pitchVelocity, camSmoothTime);

            transform.eulerAngles = new Vector3(0, transform.eulerAngles.y, 0);


            Vector3 shoulderPivot = cameraPivot.position
                                   + cameraPivot.right * shoulderOffset
                                   + Vector3.up * heightOffset;

            Vector3 desiredDir = Quaternion.Euler(_pitch, _yaw, 0) * Vector3.forward;


            int collisionMask = cameraCollisionMask & ~(1 << gameObject.layer);
            float finalDistance = _currentDistance;
            if (Physics.SphereCast(shoulderPivot, cameraCollisionRadius, -desiredDir, out RaycastHit hit, _currentDistance, collisionMask, QueryTriggerInteraction.Ignore)
                && hit.transform != transform && !hit.transform.IsChildOf(transform))
            {
                finalDistance = Mathf.Max(hit.distance - cameraCollisionBuffer, 0.2f);
            }

            Vector3 nextCamPos = shoulderPivot - (desiredDir * finalDistance);

            _camTransform.position = nextCamPos;
            _camTransform.LookAt(shoulderPivot);
        }



        private void HandleCursorToggle()
        {
            if (Keyboard.current == null) return;

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                _skipNextMouseDelta = true;
            }
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 dir = Vector2.zero;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) dir.y += 1;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) dir.y -= 1;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) dir.x += 1;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) dir.x -= 1;
            }

            if (Gamepad.current != null)
            {
                Vector2 stick = Gamepad.current.leftStick.ReadValue();
                if (stick.magnitude > dir.magnitude) dir = stick;
            }

            return dir;
        }

        private Vector2 ReadLookInput()
        {
            Vector2 delta = Vector2.zero;

            if (Mouse.current != null)
            {
                delta += Mouse.current.delta.ReadValue();
            }

            if (Gamepad.current != null)
            {
                delta += Gamepad.current.rightStick.ReadValue() * 20f * Time.deltaTime;
            }

            return delta;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, grassPushRadius);

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * groundCheckOffset, 0.2f);
        }
    }
}
