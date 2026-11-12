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
            ThirdPerson = 2,        // "Perseguição"
            FirstPerson = 4,        // dentro do carrinho; quando ele roda ou sai da pista, corta para ThirdPerson
            Tv = 5,                 // "Transmissão de TV"
            TopFollow = 6,          // "De cima": a 3D do construtor, centrada nos carrinhos dos jogadores
            // 1 (3D) e 3 (Dinâmica) saíram: o RaceController troca esses valores salvos por Tv.
        }

        // "Primeira pessoa": o carrinho entra em "rodando" quando a derrapagem passa de SpinIn (ou está fora do trilho),
        // e volta à primeira pessoa quando está no trilho com derrapagem abaixo de SpinOut (histerese, sem piscar).
        // Derrapagem 0,75 ≈ 25° de guinada visível (YawMax 38° × slip^1,4); 0,3 cortava com só ~9°.
        const float SpinIn = 0.75f, SpinOut = 0.25f;
        readonly bool[] spun = new bool[RaceSim.MaxCars];
        float nearDefault = 0.3f;

        public float minSize = 2f, maxSize = 110f;     // a pista de 8 carros é 4× maior no mundo
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
        readonly Vector3[] raceCarPos = new Vector3[RaceSim.MaxCars];
        readonly float[] raceCarHeading = new float[RaceSim.MaxCars];
        int raceCars = 2;            // carrinhos na pista (2 ou 4)
        bool hasRaceTargets;
        int focusCar = -1;           // carrinho que a vista dinâmica acompanha de perto; -1 enquadra todos

        float distanceScale = 1f;
        float trackScale = 1f;                                // TrackMath.Scale da pista (1, 2 ou 3)
        readonly Vector3[] raceAhead = new Vector3[RaceSim.MaxCars];        // ponto do trilho à frente de cada carrinho
        Rect trackRect; float trackTop;                       // contorno da pista (x, z) e a altura máxima
        Vector3[] tvSpots; int tvSpot = -1; float tvTime; Vector3 tvLook;

        const float ChaseBack = 3.2f, ChaseUp = 1.7f;         // em células: ~28° de cima
        const float ChaseLookAhead = 0.55f;                   // 0 = olha o carrinho, 1 = olha o ponto à frente
        const float ChaseTurnRate = 6f, ChasePosRate = 6f;
        const float TvMargin = 2f, TvFrame = 2.2f, TvHold = 2.5f, TvSwitch = 1.35f, TvLookRate = 8f;

        // "Atrás do carro": a câmera segue a direção suavizada do carro, não o rumo bruto. Ela gira no máximo
        // ThirdTurnRate graus por segundo, então um rodopio do carrinho vira uma panorâmica, não um tranco.
        const float ThirdTurnRate = 110f;     // graus por segundo, só enquanto o carrinho roda
        const float ThirdLookRate = 12f;      // suavização do ponto olhado
        Vector3 chaseFwd;                     // direção suavizada, no plano XZ
        Vector3 chaseLook;                    // ponto olhado suavizado
        bool chaseReady;                      // false = começa a seguir do zero (troca de modo ou de carrinho)

        void Awake()
        {
            cam = GetComponent<Camera>();
            nearDefault = cam.nearClipPlane;
            focus = new Vector3(transform.position.x, 0f, transform.position.z);
            size = cam.orthographicSize;
            pitch = tiltPitch;
            maxSize = Mathf.Max(maxSize, 110f);           // a cena guarda o valor antigo (40): a pista de 6 não cabe nele
        }

        public void ToggleTilt() => Tilted = !Tilted;

        // Zoom pelos botões da barra do construtor (k < 1 aproxima): no centro da vista, como a roda.
        public void ZoomStep(float k)
        {
            if (Tilted) distance = Mathf.Clamp(distance * k, 4f, 300f);
            else size = Mathf.Clamp(size * k, minSize, maxSize);
        }

        public void SetRaceViewMode(RaceViewMode mode)
        {
            RaceMode = mode;
            tvSpot = -1;
            followReady = false;
            panning = orbiting = false;
            chaseReady = false;
            if (cam != null) cam.nearClipPlane = nearDefault;
        }

        // Estado de um carrinho vindo da simulação: no trilho (OnSlot) e a derrapagem. Com histerese, marca "rodando".
        public void SetCarTrack(int k, bool onSlot, float slip)
        {
            if (k < 0 || k >= spun.Length) return;
            bool before = spun[k];
            if (!onSlot || slip >= SpinIn) spun[k] = true;
            else if (slip < SpinOut) spun[k] = false;
            if (spun[k] != before && k == ThirdPersonCar) chaseReady = false;   // o corte para a traseira começa do rumo atual
        }

        // A câmera mostra o interior deste carrinho? (só a primeira pessoa, e só do carrinho seguido)
        public bool InFirstPersonOf(int k) => RaceMode == RaceViewMode.FirstPerson && k == ThirdPersonCar && !spun[k];

        public void SetThirdPersonCar(int idx)
        {
            int k = Mathf.Clamp(idx, 0, raceCarPos.Length - 1);
            if (k != ThirdPersonCar) chaseReady = false;
            ThirdPersonCar = k;
        }

        // Pista nova: contorno (x, z) em mundo, altura máxima e escala. Recalcula as câmeras da TV.
        public void SetTrackFrame(Rect r, float top, float scale)
        {
            trackRect = r; trackTop = top; trackScale = scale;
            tvSpots = null;
        }

        // Carrinhos que a "De cima" mantém na tela (os dos jogadores humanos; no online, só o seu).
        readonly bool[] followCar = new bool[RaceSim.MaxCars];
        public void SetFollowCars(bool[] mask) { for (int k = 0; k < followCar.Length; k++) followCar[k] = k < mask.Length && mask[k]; }

        public void SetCarAhead(int k, Vector3 p) { if (k >= 0 && k < raceAhead.Length) raceAhead[k] = p; }

        public void SetDistanceScale(float scale) => distanceScale = Mathf.Clamp(scale, 0.7f, 1.6f);

        // Posição e rumo dos carrinhos (count = 2 ou 4). focus: o que a vista dinâmica acompanha (-1 enquadra todos).
        public void SetRaceTargets(Vector3[] pos, float[] heading, int count, int focus = -1)
        {
            raceCars = Mathf.Clamp(count, 1, raceCarPos.Length);
            for (int k = 0; k < raceCars; k++) { raceCarPos[k] = pos[k]; raceCarHeading[k] = heading[k]; }
            focusCar = focus >= 0 && focus < raceCars ? focus : -1;
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
            distance = Mathf.Clamp(halfWidth / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad), 6f, 260f);
        }

        void Update()
        {
            var m = Mouse.current;
            var kb = Keyboard.current;
            bool allowTiltToggle = RaceMode == RaceViewMode.Atual;
            if (allowTiltToggle && kb != null && kb.vKey.wasPressedThisFrame && !BuilderPanel.IsTyping) ToggleTilt();
            if (m == null) return;
            if (RaceMode == RaceViewMode.TopFollow) { TopFollowInput(m); return; }

            bool autoFollow = RaceMode != RaceViewMode.Atual;

            Vector2 mouse = m.position.ReadValue();
            bool overUi = BuilderPanel.PointerOverUI(mouse);

            float scroll = m.scroll.ReadValue().y;
            if (!overUi && !autoFollow && Mathf.Abs(scroll) > 0.01f)
            {
                float k = Mathf.Pow(0.9f, Mathf.Sign(scroll));
                if (Tilted) distance = Mathf.Clamp(distance * k, 4f, 300f);
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
            if (RaceMode == RaceViewMode.FirstPerson && hasRaceTargets)
            {
                if (spun[Mathf.Clamp(ThirdPersonCar, 0, raceCars - 1)]) ApplyThirdPerson();
                else ApplyFirstPerson();
                return;
            }
            if (RaceMode == RaceViewMode.ThirdPerson && hasRaceTargets)
            {
                ApplyThirdPerson();
                return;
            }
            if (RaceMode == RaceViewMode.Tv && hasRaceTargets)
            {
                ApplyTv();
                return;
            }
            if (RaceMode == RaceViewMode.TopFollow && hasRaceTargets)
            {
                ApplyTopFollow();
                return;
            }

            float targetBlend = Tilted ? 1f : 0f;
            blend = Mathf.MoveTowards(blend, targetBlend, Time.unscaledDeltaTime * 2.5f);
            Apply();
        }

        // "Perseguição": atrás e acima do carrinho (~28°), olhando para um ponto entre ele e o trilho à frente
        // (raceAhead), para a curva aparecer antes de chegar. Distâncias em células × trackScale (pista de 4/6 maior).
        void ApplyThirdPerson()
        {
            int k = Mathf.Clamp(ThirdPersonCar, 0, raceCars - 1);
            float rad = raceCarHeading[k] * Mathf.Deg2Rad;
            Vector3 carFwd = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            float dt = Time.unscaledDeltaTime;
            float u = trackScale * distanceScale;
            Vector3 car = raceCarPos[k];
            // Rodando (ou fora do trilho), o ponto à frente não vale: olha só para o carrinho.
            Vector3 look = spun[k] ? car : Vector3.Lerp(car, raceAhead[k], ChaseLookAhead);

            cam.nearClipPlane = nearDefault;
            if (!chaseReady)
            {
                chaseFwd = carFwd;
                chaseLook = look;
                transform.position = car - carFwd * ChaseBack * u + Vector3.up * ChaseUp * u;
                chaseReady = true;
            }

            if (spun[k])
                chaseFwd = Vector3.RotateTowards(chaseFwd, carFwd, ThirdTurnRate * Mathf.Deg2Rad * dt, 0f).normalized;
            else
                chaseFwd = Vector3.Slerp(chaseFwd, carFwd, 1f - Mathf.Exp(-ChaseTurnRate * dt)).normalized;

            chaseLook = Vector3.Lerp(chaseLook, look, 1f - Mathf.Exp(-ThirdLookRate * dt));
            Vector3 desired = car - chaseFwd * ChaseBack * u + Vector3.up * ChaseUp * u;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-ChasePosRate * dt));
            transform.rotation = Quaternion.LookRotation((chaseLook - transform.position).normalized, Vector3.up);

            cam.orthographic = false;
            cam.fieldOfView = 55f;
        }

        // Dentro do carrinho: o olho fica um pouco à frente do centro da cabine e olha reto para a frente (horizonte nivelado).
        void ApplyFirstPerson()
        {
            int k = Mathf.Clamp(ThirdPersonCar, 0, raceCars - 1);
            float rad = raceCarHeading[k] * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));
            Vector3 eye = raceCarPos[k] + fwd * 0.02f + Vector3.up * 0.155f;
            Vector3 look = raceCarPos[k] + fwd * 2f + Vector3.up * 0.1f;
            transform.SetPositionAndRotation(eye, Quaternion.LookRotation((look - eye).normalized, Vector3.up));
            cam.orthographic = false;
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.02f;
        }

        // "Transmissão de TV": câmeras fixas em volta da mesa (4 cantos e o meio de cada lado, fora do contorno da
        // pista). Fica na câmera mais perto do carrinho seguido; troca (corte seco) quando outra está TvSwitch vezes
        // mais perto e a atual já ficou TvHold segundos. Gira e dá zoom (campo de visão) acompanhando o carrinho.
        void BuildTvSpots()
        {
            Rect r = trackRect;
            float m = TvMargin * trackScale + 0.08f * Mathf.Max(r.width, r.height);
            float h = trackTop + Mathf.Clamp(0.22f * Mathf.Max(r.width, r.height), 3f * trackScale, 14f * trackScale);
            float x0 = r.xMin - m, x1 = r.xMax + m, z0 = r.yMin - m, z1 = r.yMax + m, xm = r.center.x, zm = r.center.y;
            tvSpots = new[]
            {
                new Vector3(x0, h, z0), new Vector3(xm, h, z0), new Vector3(x1, h, z0), new Vector3(x1, h, zm),
                new Vector3(x1, h, z1), new Vector3(xm, h, z1), new Vector3(x0, h, z1), new Vector3(x0, h, zm),
            };
            tvSpot = -1;
            followReady = false;
        }

        void ApplyTv()
        {
            if (tvSpots == null) BuildTvSpots();
            int k = Mathf.Clamp(focusCar >= 0 ? focusCar : ThirdPersonCar, 0, raceCars - 1);
            Vector3 car = raceCarPos[k];
            float dt = Time.unscaledDeltaTime;

            int best = 0;
            for (int i = 1; i < tvSpots.Length; i++)
                if ((tvSpots[i] - car).sqrMagnitude < (tvSpots[best] - car).sqrMagnitude) best = i;
            tvTime += dt;
            bool cut = tvSpot < 0;
            if (!cut && best != tvSpot && tvTime >= TvHold)
                cut = Vector3.Distance(tvSpots[best], car) * TvSwitch < Vector3.Distance(tvSpots[tvSpot], car);
            if (cut) { tvSpot = best; tvTime = 0f; tvLook = car; }

            tvLook = Vector3.Lerp(tvLook, car, 1f - Mathf.Exp(-TvLookRate * dt));
            Vector3 eye = tvSpots[tvSpot];
            float dist = Vector3.Distance(eye, tvLook);
            float half = TvFrame * trackScale * distanceScale;
            float fov = Mathf.Clamp(2f * Mathf.Atan(half / Mathf.Max(dist, 0.1f)) * Mathf.Rad2Deg, 8f, 60f);
            cam.orthographic = false;
            cam.nearClipPlane = nearDefault;
            cam.fieldOfView = cut ? fov : Mathf.Lerp(cam.fieldOfView, fov, 1f - Mathf.Exp(-4f * dt));
            transform.SetPositionAndRotation(eye, Quaternion.LookRotation((tvLook - eye).normalized, Vector3.up));
        }

        // "De cima" na corrida: a 3D do construtor (botão direito gira, roda aproxima), mas o centro é o meio dos
        // carrinhos de followCar, com lerp. A distância é a da roda, aumentada quando preciso para nenhum desses
        // carrinhos ficar fora da faixa livre da tela (placar em cima, painéis dos jogadores embaixo).
        const float FollowCenterRate = 10f, FollowOut = 8f, FollowIn = 1.5f;
        // Faixa livre do viewport (y de baixo para cima): fora dos HUDs. Na tela dividida, cada quadro tem a sua.
        Rect safe = Rect.MinMaxRect(0.06f, 0.24f, 0.94f, 0.90f);
        public void SetSafeArea(Rect r) => safe = r;
        float followYaw, followPitch = 52f, followDist = 14f, followDistNow = 14f;
        Vector3 followCenter; bool followReady;

        void TopFollowInput(Mouse m)
        {
            Vector2 mouse = m.position.ReadValue();
            // Na tela dividida, só a câmera do quadro onde está o mouse responde.
            bool overUi = BuilderPanel.PointerOverUI(mouse) || !cam.pixelRect.Contains(mouse);
            float scroll = m.scroll.ReadValue().y;
            if (!overUi && Mathf.Abs(scroll) > 0.01f)
                followDist = Mathf.Clamp(followDist * Mathf.Pow(0.9f, Mathf.Sign(scroll)), 3f, 120f);
            if (m.rightButton.wasPressedThisFrame && !overUi) orbiting = true;
            if (!m.rightButton.isPressed) orbiting = false;
            if (orbiting)
            {
                Vector2 d = mouse - lastMouse;
                followYaw += d.x * 0.25f;
                followPitch = Mathf.Clamp(followPitch - d.y * 0.2f, 18f, 89f);
            }
            lastMouse = mouse;
        }

        void ApplyTopFollow()
        {
            Vector3 lo = Vector3.zero, hi = Vector3.zero; int n = 0;
            for (int k = 0; k < raceCars; k++)
            {
                if (!followCar[k]) continue;
                lo = n == 0 ? raceCarPos[k] : Vector3.Min(lo, raceCarPos[k]);
                hi = n == 0 ? raceCarPos[k] : Vector3.Max(hi, raceCarPos[k]);
                n++;
            }
            if (n == 0) lo = hi = raceCarPos[0];
            Vector3 center = (lo + hi) * 0.5f;
            float dt = Time.unscaledDeltaTime;
            followCenter = followReady ? Vector3.Lerp(followCenter, center, 1f - Mathf.Exp(-FollowCenterRate * dt)) : center;

            cam.orthographic = false;
            cam.nearClipPlane = nearDefault;
            cam.fieldOfView = fieldOfView;
            var rot = Quaternion.Euler(followPitch, followYaw, 0f);
            // O centro aparece no meio da faixa livre, não no meio da tela: a câmera mira pelo raio dessa altura.
            float t = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 aim = rot * new Vector3(0f, ((safe.yMin + safe.yMax) - 1f) * t, 1f);

            // Menor distância (a partir da roda) em que todos os carrinhos seguidos ficam dentro da faixa livre.
            float need = followDist * trackScale;          // followDist em células: igual nas pistas de 2, 4 e 6
            for (int i = 0; i < 30; i++)
            {
                transform.SetPositionAndRotation(followCenter - aim * need, rot);
                if (AllFollowedInside()) break;
                need *= 1.12f;
            }
            if (!followReady) followDistNow = need;
            else followDistNow = Mathf.Lerp(followDistNow, need, 1f - Mathf.Exp(-(need > followDistNow ? FollowOut : FollowIn) * dt));
            followReady = true;
            transform.SetPositionAndRotation(followCenter - aim * followDistNow, rot);
        }

        bool AllFollowedInside()
        {
            for (int k = 0; k < raceCars; k++)
            {
                if (!followCar[k]) continue;
                Vector3 v = cam.WorldToViewportPoint(raceCarPos[k]);
                if (v.z <= 0f || !safe.Contains(new Vector2(v.x, v.y))) return false;
            }
            return true;
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
