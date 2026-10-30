using UnityEngine;
using UnityEngine.InputSystem;

namespace Autorama
{
    // Duas vistas, com transição suave (tecla V):
    // - de cima, ortográfica: arrastar com o botão direito ou do meio move a vista; a roda dá zoom no ponto do mouse;
    // - 3D inclinada, em perspectiva: botão direito gira em volta da pista, o do meio move, a roda aproxima.
    [RequireComponent(typeof(Camera))]
    public class TopDownCamera : MonoBehaviour
    {
        public float minSize = 2f, maxSize = 40f;
        public float tiltPitch = 52f, fieldOfView = 32f;

        public bool Tilted { get; private set; }
        public bool IsTopView => blend <= 0.001f;      // réguas e células só fazem sentido na vista de cima
        public Camera Cam => cam;

        Camera cam;
        Vector3 focus;                 // ponto da mesa no centro da vista
        float size = 11f;              // meia altura visível na vista de cima
        float yaw, pitch, distance = 25f;
        float blend;                   // 0 = de cima, 1 = 3D
        Vector3 dragOrigin;
        bool panning, orbiting;
        Vector2 lastMouse;

        void Awake()
        {
            cam = GetComponent<Camera>();
            focus = new Vector3(transform.position.x, 0f, transform.position.z);
            size = cam.orthographicSize;
            pitch = tiltPitch;
        }

        public void ToggleTilt() => Tilted = !Tilted;

        // A pista foi empurrada na mesa: a vista anda junto, para nada pular na tela.
        public void Nudge(Vector2 cells) => focus += new Vector3(cells.x, 0f, cells.y);

        public Vector3 MouseOnTable() => ScreenOnTable(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);

        public Vector3 ScreenOnTable(Vector2 screen)
        {
            Ray ray = cam.ScreenPointToRay(screen);
            return new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float enter) ? ray.GetPoint(enter) : focus;
        }

        // Enquadra um retângulo da mesa (x, z), deixando espaço para o painel da esquerda.
        public void Frame(Rect r, float leftPanelPixels = 0f)
        {
            float aspect = cam.aspect;
            float usable = Mathf.Clamp(1f - leftPanelPixels / Mathf.Max(1f, Screen.width), 0.3f, 1f);
            size = Mathf.Clamp(Mathf.Max(r.height / 2f, r.width / 2f / (aspect * usable)) + 1.2f, minSize, maxSize);
            // Desloca o centro para a direita, para a pista não ficar embaixo do painel.
            float shift = (1f - usable) * size * aspect;
            focus = new Vector3(r.center.x - shift, 0f, r.center.y);
            // Na 3D, a distância que mostra a pista inteira.
            // A pista vista inclinada encurta na profundidade; a largura é que manda (com a tela mais larga que alta).
            float halfWidth = Mathf.Max(r.width * 0.5f / (aspect * usable), r.height * 0.5f) + 0.8f;
            distance = Mathf.Clamp(halfWidth / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad), 6f, 120f);
        }

        void Update()
        {
            var m = Mouse.current;
            var kb = Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame && !BuilderPanel.IsTyping) ToggleTilt();
            if (m == null) return;

            Vector2 mouse = m.position.ReadValue();
            bool overUi = BuilderPanel.PointerOverUI(mouse);

            float scroll = m.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f)
            {
                float k = Mathf.Pow(0.9f, Mathf.Sign(scroll));
                if (Tilted) distance = Mathf.Clamp(distance * k, 4f, 150f);
                else
                {
                    Vector3 before = MouseOnTable();
                    size = Mathf.Clamp(size * k, minSize, maxSize);
                    Apply();
                    focus += before - MouseOnTable();         // mantém o ponto sob o mouse
                }
            }

            // Botão do meio sempre move a vista; o direito move na vista de cima e gira na 3D.
            bool right = m.rightButton.isPressed, middle = m.middleButton.isPressed;
            if ((m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame) && !overUi)
            {
                orbiting = Tilted && m.rightButton.wasPressedThisFrame;
                panning = !orbiting;
                dragOrigin = MouseOnTable();
                lastMouse = mouse;
            }
            if (!right && !middle) { panning = orbiting = false; }

            if (orbiting)
            {
                Vector2 d = mouse - lastMouse;
                yaw += d.x * 0.25f;
                pitch = Mathf.Clamp(pitch - d.y * 0.2f, 18f, 85f);
            }
            else if (panning)
            {
                focus += dragOrigin - MouseOnTable();
                Apply();
            }
            lastMouse = mouse;
        }

        void LateUpdate()
        {
            blend = Mathf.MoveTowards(blend, Tilted ? 1f : 0f, Time.unscaledDeltaTime * 2.5f);
            Apply();
        }

        void Apply()
        {
            if (cam == null) return;
            if (blend <= 0.001f)
            {
                cam.orthographic = true;
                cam.orthographicSize = size;
                transform.SetPositionAndRotation(focus + Vector3.up * 30f, Quaternion.Euler(90f, 0f, 0f));
                return;
            }
            // Transição: a perspectiva começa de cima, na distância que mostra a mesma área da vista ortográfica.
            float e = Mathf.SmoothStep(0f, 1f, blend);
            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            float topDistance = size / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            var rot = Quaternion.Euler(Mathf.Lerp(89.9f, pitch, e), Mathf.Lerp(0f, yaw, e), 0f);
            float d = Mathf.Lerp(topDistance, distance, e);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * d, rot);
        }
    }
}
