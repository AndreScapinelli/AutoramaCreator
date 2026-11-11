using System;
using UnityEngine;

namespace Autorama
{
    // Som da corrida (seção 29 do guia). Tudo é sintetizado na hora, sem arquivos de áudio:
    // o zumbido do motor elétrico de cada carrinho (o tom sobe com a velocidade), o chiado do pneu derrapando,
    // os bipes da largada, a batida, a saída de pista, a volta completa e a vitória.
    public partial class RaceController
    {
        const int Rate = 44100;
        AudioSource[] motor, skid, jet;
        AudioSource fx;
        AudioClip beepClip, goClip, hitClip, offClip, lapClip, winClip, switchClip, turboOnClip, turboFillClip;
        // Turbo (seção 37): o sopro contínuo de cada carrinho, se o turbo estava soprando e a carga do tanque no quadro anterior.
        readonly float[] jetVol = new float[RaceSim.MaxCars], lastTank = new float[RaceSim.MaxCars];
        readonly bool[] jetOn = new bool[RaceSim.MaxCars];
        int lastLights = -1, lastWinner = -1;
        RaceSim.Phase audioPhase;
        readonly int[] lastOffs = new int[RaceSim.MaxCars];
        readonly float[] motorVol = new float[RaceSim.MaxCars];

        static AudioClip Clip(string name, float seconds, Func<float, int, float> f)
        {
            int n = Mathf.RoundToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, i), -1f, 1f);
            var c = AudioClip.Create(name, n, 1, Rate, false);
            c.SetData(data, 0);
            return c;
        }

        class MotorSound { public string name; public AudioClip clip; public float p0, p1, vol; }
        readonly System.Collections.Generic.List<MotorSound> motors = new System.Collections.Generic.List<MotorSound>();
        MotorSound MotorNow => motors[Mathf.Clamp(set.motorSound, 0, motors.Count - 1)];
        public string MotorName => motors.Count > 0 ? MotorNow.name : "";
        public void NextMotor() { set.motorSound = (Mathf.Clamp(set.motorSound, 0, motors.Count - 1) + 1) % motors.Count; set.Save(); }

        // Lê um WAV simples (PCM de 16 bits, mono ou estéreo) e devolve um clipe mono. null se não existe ou não é esse formato.
        static AudioClip LoadWav(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var b = System.IO.File.ReadAllBytes(path);
                if (b.Length < 44 || System.Text.Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || System.Text.Encoding.ASCII.GetString(b, 8, 4) != "WAVE") return null;
                int pos = 12, channels = 1, rate = Rate, bits = 16, dataAt = -1, dataLen = 0;
                while (pos + 8 <= b.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                    int len = BitConverter.ToInt32(b, pos + 4);
                    if (id == "fmt ") { channels = BitConverter.ToInt16(b, pos + 10); rate = BitConverter.ToInt32(b, pos + 12); bits = BitConverter.ToInt16(b, pos + 22); }
                    else if (id == "data") { dataAt = pos + 8; dataLen = Mathf.Min(len, b.Length - dataAt); break; }
                    pos += 8 + len + (len & 1);
                }
                if (dataAt < 0 || bits != 16 || channels < 1) { Debug.LogWarning("Autorama: motor.wav precisa ser PCM de 16 bits."); return null; }
                int n = dataLen / (2 * channels);
                if (n < rate / 10) return null;
                var data = new float[n];
                for (int i = 0; i < n; i++)
                {
                    float v = 0f;
                    for (int c = 0; c < channels; c++) v += BitConverter.ToInt16(b, dataAt + (i * channels + c) * 2) / 32768f;
                    data[i] = v / channels;
                }
                var clip = AudioClip.Create("Motor: arquivo", n, 1, rate, false);
                clip.SetData(data, 0);
                return clip;
            }
            catch (Exception e) { Debug.LogWarning("Autorama: não deu para ler motor.wav: " + e.Message); return null; }
        }

        static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        // Ataque curto e queda exponencial: evita o estalo no começo e no fim.
        static float Env(float t, float dur, float decay) => Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-decay * t) * Mathf.Min(1f, (dur - t) / 0.01f);

        void AudioStart()
        {
            if (FindFirstObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
            var rnd = new System.Random(7);
            float Noise() => (float)(rnd.NextDouble() * 2.0 - 1.0);

            // Motor: silvo agudo e fino, como turbina de broca de dentista. 1 s que emenda sem costura (frequências
            // inteiras). Duas notas quase iguais (1400 e 1409 Hz) batem entre si e dão o "zzz" vibrado; a oitava e a
            // quinta acima deixam o som metálico; um chiado de ar bem leve por cima. Sem parte grave.
            // O tom vem do "pitch" da fonte: de ~770 Hz parado a ~3,4 kHz no fundo.
            var hiss = new float[Rate];
            {
                // Ruído só nos agudos (diferença entre amostras vizinhas), com as pontas cruzadas para emendar.
                float prev = 0f;
                for (int i = 0; i < Rate; i++) { float n = Noise(); hiss[i] = (n - prev) * 0.5f; prev = n; }
                int fade = Rate / 20;
                for (int i = 0; i < fade; i++) { float w = i / (float)fade; hiss[i] = hiss[i] * w + hiss[Rate - fade + i] * (1f - w); }
            }
            var broca = Clip("Motor: broca", 1f, (t, i) =>
                0.3f * Sin(1400f, t) + 0.26f * Sin(1409f, t) + 0.16f * Sin(2800f, t) + 0.1f * Sin(4200f, t) + 0.05f * Sin(5609f, t)
                + 0.06f * hiss[i]);

            // Onda dente de serra suavizada (soma de harmônicos até "top"), para os motores com zumbido.
            float Saw(float hz, float t, int top) { float v = 0f; for (int h = 1; h <= top; h++) v += Sin(hz * h, t) / h; return v * 0.55f; }
            // As opções de motor (o jogador alterna nos Ajustes). p0 e p1: tom parado e no fundo; vol: volume relativo.
            motors.Clear();
            motors.Add(new MotorSound { name = "Broca", clip = broca, p0 = 0.55f, p1 = 2.45f, vol = 0.62f });
            // Carrinho de controle remoto: silvo médio e limpo, quase só uma nota com um pouco de oitava.
            motors.Add(new MotorSound { name = "Elétrico suave", p0 = 0.6f, p1 = 2.6f, vol = 0.9f,
                clip = Clip("Motor: elétrico", 1f, (t, i) => 0.5f * Sin(420f, t) + 0.18f * Sin(840f, t) + 0.07f * Sin(1260f, t) + 0.04f * Sin(423f, t)) });
            // Autorama antigo: motor de escovas, zumbido rasgado com a "areia" do coletor.
            motors.Add(new MotorSound { name = "Escovas", p0 = 0.6f, p1 = 2.4f, vol = 0.8f,
                clip = Clip("Motor: escovas", 1f, (t, i) => Saw(170f, t, 9) * (0.75f + 0.25f * Sin(340f, t)) + 0.05f * hiss[i]) });
            // Turbina: mais ar que nota. Chiado forte com um silvo fino por dentro.
            motors.Add(new MotorSound { name = "Turbina", p0 = 0.6f, p1 = 2.0f, vol = 0.4f,     // a escolhida; volume reduzido a pedido
                clip = Clip("Motor: turbina", 1f, (t, i) => 0.3f * hiss[i] * (0.7f + 0.3f * Sin(40f, t)) + 0.25f * Sin(2100f, t) + 0.12f * Sin(2112f, t)) });
            // Grave e redondo: ronco baixo, para quem prefere o motor "de carro".
            motors.Add(new MotorSound { name = "Ronco", p0 = 0.7f, p1 = 2.8f, vol = 1.1f,
                clip = Clip("Motor: ronco", 1f, (t, i) => Saw(70f, t, 6) * 0.9f + 0.2f * Sin(35f, t)) });
            // Gravação do jogador: autorama/sons/motor.wav (PCM 16 bits). Se existir, vira a 6ª opção.
            var own = LoadWav(System.IO.Path.Combine(TrackLibrary.Root, "sons", "motor.wav"));
            if (own != null) motors.Add(new MotorSound { name = "Seu arquivo (motor.wav)", clip = own, p0 = 0.6f, p1 = 2.0f, vol = 1f });
            var motorClip = MotorNow.clip;

            // Pneu derrapando: ruído passado por um filtro estreito em volta de 2,2 kHz (ressonador de dois polos),
            // com as pontas cruzadas para o laço não estalar.
            int sn = Rate;
            var raw = new float[sn];
            {
                float f0 = 2200f, r = 0.985f, a1 = 2f * r * Mathf.Cos(2f * Mathf.PI * f0 / Rate), a2 = -r * r, y1 = 0f, y2 = 0f, peak = 0f;
                for (int i = 0; i < sn; i++) { float y = Noise() + a1 * y1 + a2 * y2; y2 = y1; y1 = y; raw[i] = y; peak = Mathf.Max(peak, Mathf.Abs(y)); }
                int fade = Rate / 20;
                for (int i = 0; i < fade; i++) { float w = i / (float)fade; raw[i] = raw[i] * w + raw[sn - fade + i] * (1f - w); }
                for (int i = 0; i < sn; i++) raw[i] = raw[i] / peak * 0.7f;
            }
            var skidClip = AudioClip.Create("Derrapagem", sn - Rate / 20, 1, Rate, false);
            skidClip.SetData(raw, 0);

            beepClip = Clip("Bipe", 0.2f, (t, i) => 0.5f * Sin(520f, t) * Env(t, 0.2f, 6f));
            goClip = Clip("Largada", 0.55f, (t, i) => 0.5f * (Sin(1040f, t) + 0.3f * Sin(2080f, t)) * Env(t, 0.55f, 3.5f));
            hitClip = Clip("Batida", 0.22f, (t, i) => (0.7f * Noise() + 0.6f * Sin(140f - 200f * t, t)) * Env(t, 0.22f, 22f));
            offClip = Clip("Saída de pista", 0.6f, (t, i) =>
            {
                // Plástico quicando: três estalos cada vez mais fracos.
                float s = 0f;
                foreach (float at in new[] { 0f, 0.17f, 0.31f })
                    if (t >= at) s += (0.8f * Noise() + 0.5f * Sin(260f, t - at)) * Mathf.Min(1f, (t - at) / 0.003f) * Mathf.Exp(-30f * (t - at)) * (1f - at * 1.8f);
                return s * 0.8f;
            });
            lapClip = Clip("Volta", 0.35f, (t, i) => 0.35f * (t < 0.12f ? Sin(880f, t) : Sin(1320f, t)) * Env(t, 0.35f, 5f));
            winClip = Clip("Vitória", 1.1f, (t, i) =>
            {
                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };     // dó, mi, sol, dó
                int k = Mathf.Min(3, (int)(t / 0.16f));
                float lt = t - k * 0.16f;
                return 0.4f * (Sin(notes[k], t) + 0.25f * Sin(notes[k] * 2f, t)) * Mathf.Min(1f, lt / 0.005f) * Mathf.Exp(-(k == 3 ? 2.5f : 5f) * lt) * Mathf.Min(1f, (1.1f - t) / 0.05f);
            });
            switchClip = Clip("Troca de trilho", 0.5f, (t, i) => 0.4f * Sin(t < 0.2f ? 660f : 440f, t) * Env(t, 0.5f, 4f));

            // Turbo. Sopro contínuo: ar forte que pulsa (o chiado do motor, mais cheio) com duas notas quase iguais por
            // dentro; 1 s que emenda sem costura. Ligar: um "vuush" que sobe de 300 a 1500 Hz em 0,35 s.
            // Tanque enchendo (saída de pista): três notas subindo, um "power-up" curto.
            var jetClip = Clip("Turbo: sopro", 1f, (t, i) => 0.55f * hiss[i] * (0.75f + 0.25f * Sin(14f, t)) + 0.14f * Sin(930f, t) + 0.1f * Sin(941f, t) + 0.06f * Sin(1860f, t));
            turboOnClip = Clip("Turbo: ligar", 0.35f, (t, i) =>
                (0.45f * Mathf.Sin(2f * Mathf.PI * (300f * t + 1714f * t * t)) + 0.25f * Noise() * (t / 0.35f)) * Mathf.Min(1f, t / 0.01f) * Mathf.Min(1f, (0.35f - t) / 0.08f));
            turboFillClip = Clip("Turbo: tanque", 0.3f, (t, i) =>
            {
                float hz = t < 0.08f ? 700f : t < 0.16f ? 930f : 1240f;
                return 0.35f * (Sin(hz, t) + 0.3f * Sin(hz * 2f, t)) * Env(t, 0.3f, 5f);
            });

            motor = new AudioSource[RaceSim.MaxCars];
            skid = new AudioSource[RaceSim.MaxCars];
            jet = new AudioSource[RaceSim.MaxCars];
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                motor[k] = Loop("Som do motor " + (k + 1), motorClip);
                skid[k] = Loop("Som do pneu " + (k + 1), skidClip);
                jet[k] = Loop("Som do turbo " + (k + 1), jetClip);
            }
            fx = gameObject.AddComponent<AudioSource>();
            fx.playOnAwake = false; fx.spatialBlend = 0f;
            sim.Spark += _ => Play(hitClip, 0.8f);
            sim.LapDone += (k, lap) => Play(lapClip, sim.aiSkill[k] > 0 ? 0.25f : 0.7f);
        }

        AudioSource Loop(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip; a.loop = true; a.playOnAwake = false; a.spatialBlend = 0f; a.volume = 0f;
            a.Play();
            return a;
        }

        float Master => set != null ? Mathf.Clamp01(set.volume / 100f) : 1f;
        void Play(AudioClip c, float vol) { if (fx != null && panel != null && panel.RaceMode && Master > 0f) fx.PlayOneShot(c, vol * Master); }

        // A cada quadro: motor e pneu de cada carrinho, e os sons que dependem da fase da corrida.
        void AudioTick()
        {
            if (motor == null || sim == null) return;
            bool on = panel.RaceMode && sim.Ready;
            float dt = Time.unscaledDeltaTime, master = Master;
            // Com 4 carrinhos cada motor toca mais baixo, para a soma não estourar.
            var ms = MotorNow;
            float each = (sim.N > 2 ? 0.16f : 0.24f) * ms.vol;
            for (int k = 0; k < RaceSim.MaxCars; k++)
            {
                // Os laços ficam sempre tocando (mudos quando não há o que ouvir); se a Unity parou algum, religa.
                if (motor[k].clip != ms.clip) { motor[k].clip = ms.clip; motor[k].Play(); }     // trocou de motor nos Ajustes
                if (!motor[k].isPlaying) motor[k].Play();
                if (!skid[k].isPlaying) skid[k].Play();
                bool car = on && k < sim.N && sim.present[k];
                var c = car ? sim.cars[k] : null;
                bool running = car && c.mode == CarMode.OnSlot && (sim.phase == RaceSim.Phase.Running || sim.phase == RaceSim.Phase.Done);
                float speed = running ? Mathf.Clamp01(c.v / Mathf.Max(1f, sim.set.vmax)) : 0f;
                // Motor: parado fica mudo; acelerando soa mais forte que embalado. Cada carrinho tem um tom um pouco diferente.
                float target = running ? (0.25f + 0.75f * c.T) * Mathf.Min(1f, speed * 6f + c.T) : 0f;
                motorVol[k] = Mathf.MoveTowards(motorVol[k], target, dt * 6f);
                motor[k].volume = motorVol[k] * each * master;
                // Sobe rápido no começo e devagar no fim (raiz), como o motor que gira livre.
                motor[k].pitch = Mathf.Lerp(ms.p0, ms.p1, Mathf.Sqrt(speed)) * (1f + 0.03f * k);
                // Pneu: aparece quando a barra de derrapagem passa de 25% e cresce até a saída de pista.
                float slip = running ? Mathf.Clamp01((c.slip - 0.25f) / 0.75f) : 0f;
                skid[k].volume = slip * slip * 0.5f * master;
                skid[k].pitch = 0.85f + 0.3f * slip + 0.05f * k;
                // Turbo: o sopro enquanto o tanque está sendo gasto, o "vuush" ao ligar e as notas quando o tanque enche.
                // O carrinho de robô soa mais baixo, como na volta completa.
                if (!jet[k].isPlaying) jet[k].Play();
                bool boosting = running && sim.set.turbo && sim.phase == RaceSim.Phase.Running && c.boost && c.turbo > 0f && c.T > 0f;
                float quiet = car && sim.aiSkill[k] > 0 ? 0.4f : 1f;
                jetVol[k] = Mathf.MoveTowards(jetVol[k], boosting ? 1f : 0f, dt * 8f);
                jet[k].volume = jetVol[k] * (sim.N > 2 ? 0.22f : 0.3f) * quiet * master;
                jet[k].pitch = 0.9f + 0.35f * speed + 0.03f * k;
                if (boosting && !jetOn[k]) Play(turboOnClip, 0.6f * quiet);
                jetOn[k] = boosting;
                float tank = car ? c.turbo : 0f;
                if (car && tank > lastTank[k] + 0.05f) Play(turboFillClip, 0.6f * quiet);
                lastTank[k] = tank;
                if (car && c.offs != lastOffs[k]) { if (c.offs > lastOffs[k]) Play(offClip, 0.9f); lastOffs[k] = c.offs; }
                if (!car) lastOffs[k] = 0;
            }
            if (!on) { lastLights = -1; lastWinner = -1; audioPhase = RaceSim.Phase.Idle; return; }

            // Largada: um bipe a cada luz vermelha e o bipe agudo na verde (também em cada manche).
            int lightsNow = sim.phase == RaceSim.Phase.Countdown ? Mathf.Min(3, Mathf.FloorToInt(sim.t / (RaceSim.Count / 3f)) + 1) : 0;
            if (lightsNow > 0 && lightsNow != lastLights) Play(beepClip, 0.7f);
            lastLights = lightsNow;
            if (sim.phase != audioPhase)
            {
                if (sim.phase == RaceSim.Phase.Running && audioPhase == RaceSim.Phase.Countdown) Play(goClip, 0.8f);
                if (sim.phase == RaceSim.Phase.Switch) Play(switchClip, 0.7f);
                audioPhase = sim.phase;
            }
            if (sim.winner != lastWinner) { if (sim.winner >= 0) Play(winClip, 0.8f); lastWinner = sim.winner; }
        }
    }
}
