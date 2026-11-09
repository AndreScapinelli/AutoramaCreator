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
        public enum RaceViewMode
        {
            Atual = 0,
            ThreeD = 1,
            ThirdPerson = 2,
            AutoDoisCarros = 3,
        }

        public float minSize = 2f, maxSize = 40f;
        public float tiltPitch = 52f, fieldOfView = 32f;

        public bool Tilted { get; private set; }
        public bool IsTopView => blend <= 0.001f;      // réguas e células só fazem sentido na vista de cima
        public Camera Cam => cam;
        public RaceViewMode RaceMode { get; private set; } = RaceViewMode.Atual;
        public int ThirdPersonCar { get; private set; }

        Camera cam;
        Vector3 focus;                 // ponto da mesa no centro da vista
        float size = 11f;              // meia altura visível na vista de cima
        float yaw, pitch, distance = 25f;
        float blend;                   // 0 = de cima, 1 = 3D
        Vector3 dragOrigin;
        bool panning, orbiting;
        Vector2 lastMouse;
        readonly Vector3[] raceCarPos = new Vector3[2];
        readonly float[] raceCarHeading = new float[2];
        bool hasRaceTargets;
        int focusCar = -1;           // carrinho que a vista dinâmica acompanha de perto; -1 enquadra os dois

        float thirdDistance = 7f, thirdHeight = 2.2f;
        float autoSize = 11f;
        float distanceScale = 1f;

        void Awake()
        {
            cam = GetComponent<Camera>();
            focus = new Vector3(transform.position.x, 0f, transform.position.z);
            size = cam.orthographicSize;
            pitch = tiltPitch;
        }

        public void ToggleTilt() => Tilted = !Tilted;

        // Zoom pelos botões da barra do construtor (k < 1 aproxima): no centro da vista, como a roda.
        public void ZoomStep(float k)
        {
            if (Tilted) distance = Mathf.Clamp(distance * k, 4f, 150f);
            else size = Mathf.Clamp(size * k, minSize, maxSize);
        }

        public void SetRaceViewMode(RaceViewMode mode)
        {
            RaceMode = mode;
            if (mode == RaceViewMode.ThreeD) Tilted = true;
            panning = orbiting = false;
        }

        public void SetThirdPersonCar(int idx) => ThirdPersonCar = Mathf.Clamp(idx, 0, 1);

        public void SetDistanceScale(float scale) => distanceScale = Mathf.Clamp(scale, 0.7f, 1.6f);

        public void SetRaceTargets(Vector3 car0Pos, float car0Heading, Vector3 car1Pos, float car1Heading, int focus = -1)
        {
            raceCarPos[0] = car0Pos;
            raceCarPos[1] = car1Pos;
            raceCarHeading[0] = car0Heading;
            raceCarHeading[1] = car1Heading;
            focusCar = focus is 0 or 1 ? focus : -1;
            hasRaceTargets = true;
        }

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
            bool allowTiltToggle = RaceMode == RaceViewMode.Atual || RaceMode == RaceViewMode.ThreeD;
            if (allowTiltToggle && kb != null && kb.vKey.wasPressedThisFrame && !BuilderPanel.IsTyping) ToggleTilt();
            if (m == null) return;

            bool autoFollow = RaceMode == RaceViewMode.ThirdPerson || RaceMode == RaceViewMode.AutoDoisCarros;

            Vector2 mouse = m.position.ReadValue();
            bool overUi = BuilderPanel.PointerOverUI(mouse);

            float scroll = m.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f)
            {
                float k = Mathf.Pow(0.9f, Mathf.Sign(scroll));
                if (RaceMode == RaceViewMode.ThirdPerson) thirdDistance = Mathf.Clamp(thirdDistance * k, 4f, 18f);
                else if (RaceMode == RaceViewMode.AutoDoisCarros)
                {
                    // Neste modo o zoom é automático, então ignoramos a roda para manter previsibilidade.
                }
                else if (Tilted) distance = Mathf.Clamp(distance * k, 4f, 150f);
                else
                {
                    Vector3 before = MouseOnTable();
                    size = Mathf.Clamp(size * k, minSize, maxSize);
                    Apply();
                    focus += before - MouseOnTable();         // mantém o ponto sob o mouse
                }
            }

            if (autoFollow) return;

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
            if (RaceMode == RaceViewMode.ThirdPerson && hasRaceTargets)
            {
                ApplyThirdPerson();
                return;
            }
            if (RaceMode == RaceViewMode.AutoDoisCarros && hasRaceTargets)
            {
                ApplyAutoDoisCarros();
                return;
            }

            float targetBlend = RaceMode == RaceViewMode.ThreeD ? 1f : Tilted ? 1f : 0f;
            blend = Mathf.MoveTowards(blend, targetBlend, Time.unscaledDeltaTime * 2.5f);
            Apply();
        }

        void ApplyThirdPerson()
        {
            int k = Mathf.Clamp(ThirdPersonCar, 0, 1);
            float rad = raceCarHeading[k] * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 target = raceCarPos[k] + fwd * 1.4f + Vector3.up * 0.25f;
            Vector3 desired = raceCarPos[k] - fwd * thirdDistance * distanceScale + Vector3.up * thirdHeight * distanceScale;

            float dt = Time.unscaledDeltaTime;
            float posLerp = 1f - Mathf.Exp(-7f * dt);
            transform.position = Vector3.Lerp(transform.position, desired, posLerp);

            var look = Quaternion.LookRotation((target - transform.position).normalized, Vector3.up);
            float rotLerp = 1f - Mathf.Exp(-9f * dt);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, rotLerp);

            cam.orthographic = false;
            cam.fieldOfView = 50f;
        }

        void ApplyAutoDoisCarros()
        {
            Vector3 a = raceCarPos[0], b = raceCarPos[1];
            Vector3 center = focusCar >= 0 ? raceCarPos[focusCar] : (a + b) * 0.5f;
            float needed;
            if (focusCar >= 0)
            {
                needed = 5.5f;
            }
            else
            {
                float halfX = Mathf.Max(Mathf.Abs(a.x - center.x), Mathf.Abs(b.x - center.x)) + 1.3f;
                float halfZ = Mathf.Max(Mathf.Abs(a.z - center.z), Mathf.Abs(b.z - center.z)) + 1.1f;
                needed = Mathf.Max(halfZ, halfX / Mathf.Max(0.1f, cam.aspect));
            }
            needed = Mathf.Clamp(needed * distanceScale, Mathf.Max(minSize, 3f), maxSize);

            float dt = Time.unscaledDeltaTime;
            float blendPos = 1f - Mathf.Exp(-8f * dt);
            focus = Vector3.Lerp(focus, new Vector3(center.x, 0f, center.z), blendPos);
            autoSize = Mathf.Lerp(autoSize, needed, 1f - Mathf.Exp(-6f * dt));

            cam.orthographic = true;
            cam.orthographicSize = autoSize;
            transform.SetPositionAndRotation(focus + Vector3.up * 30f, Quaternion.Euler(90f, 0f, 0f));
        }

        void Apply()
        {
            if (cam == null) return;
            if (blend <= 0.001f)
            {
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Clamp(size * distanceScale, minSize, maxSize);
                transform.SetPositionAndRotation(focus + Vector3.up * 30f, Quaternion.Euler(90f, 0f, 0f));
                return;
            }
            // Transição: a perspectiva começa de cima, na distância que mostra a mesma área da vista ortográfica.
            float e = Mathf.SmoothStep(0f, 1f, blend);
            cam.orthographic = false;
            cam.fieldOfView = fieldOfView;
            float topDistance = size * distanceScale / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            var rot = Quaternion.Euler(Mathf.Lerp(89.9f, pitch, e), Mathf.Lerp(0f, yaw, e), 0f);
            float d = Mathf.Lerp(topDistance, distance * distanceScale, e);
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * d, rot);
        }
    }
}
