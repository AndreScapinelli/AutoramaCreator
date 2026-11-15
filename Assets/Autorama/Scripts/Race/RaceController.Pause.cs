using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Pausa no P (seção 53 do guia), na corrida livre, no campeonato e no online. Parada, a simulação não anda (o relógio
    // da prova também não) e os motores ficam mudos; ao continuar há 3 s de contagem antes de andar de novo.
    // Online: o visitante manda o P num "in" (pz, um contador, como o de recolocar); quem decide é o host, que avisa
    // todos num ev "pause" (repetido a cada 1 s enquanto pausado, para quem entrar no meio). Cada piloto pausa até
    // PauseMax vezes por bateria; continua quem pausou ou o host, e a corrida volta sozinha depois de PauseLong.
    public partial class RaceController
    {
        bool paused, pauseShown;
        string pausedBy;                                    // online: o id de quem pausou
        float pausedAt, resumeLeft, pauseBcastAcc;
        int myPz;                                           // visitante: quantas vezes apertei P
        readonly Dictionary<string, int> remotePz = new Dictionary<string, int>();    // host: o contador de cada visitante
        readonly Dictionary<string, int> pauseUsed = new Dictionary<string, int>();   // host: pausas de cada piloto nesta bateria
        const int PauseMax = 3;
        const float PauseLong = 60f, PauseResume = 3f;
        VisualElement pauseBox;
        Button pauseGo, pauseGrid;

        bool Paused => paused || resumeLeft > 0f;
        bool CanPause => sim != null && panel.RaceMode && (sim.phase == RaceSim.Phase.Running || sim.phase == RaceSim.Phase.Countdown || sim.phase == RaceSim.Phase.Switch);

        void BuildPauseUI()
        {
            pauseBox = El("res-btns", banner);
            pauseBox.style.display = DisplayStyle.None;
            pauseGo = Btn("Continuar  (P)", () => PausePressed(OnlineRun ? myId : null), "btn btn--accent res-btn", pauseBox);
            pauseGrid = Btn("Voltar ao grid", () =>
            {
                PauseClear();
                if (ChampLike) ChampRestart(); else { sim.Reset(); scoreDirty = true; }
            }, "btn res-btn", pauseBox);
        }

        void PauseClear() { paused = false; resumeLeft = 0f; pausedBy = null; pauseUsed.Clear(); }

        // Apertou P (ou o botão Continuar). who: o id do piloto no online; null no offline.
        void PausePressed(string who)
        {
            if (OnlineRun && !IsHost) { myPz++; SendIn(0); return; }      // o host decide
            if (paused)
            {
                if (!OnlineRun || who == myId || who == pausedBy) PauseResume_();
                return;
            }
            if (resumeLeft > 0f || !CanPause) return;
            if (OnlineRun)
            {
                if (who != myId && (!HeatNow(out _, out _, out _, out _, out var pl) || LaneOf(pl, who) < 0)) return;   // só quem corre (e o host)
                int used = pauseUsed.TryGetValue(who, out int u) ? u : 0;
                if (used >= PauseMax) { if (who == myId) panel.Toast($"Você já pausou {PauseMax} vezes nesta bateria."); return; }
                pauseUsed[who] = used + 1;
            }
            paused = true; pausedBy = who; pausedAt = Time.realtimeSinceStartup;
            if (OnlineRun) PauseBroadcast();
        }

        void PauseResume_()
        {
            paused = false; resumeLeft = PauseResume;
            if (OnlineRun && IsHost) PauseBroadcast();
        }

        void PauseBroadcast()
        {
            pauseBcastAcc = 0f;
            net.Send(new NetOutEv { k = "pause", pz = paused ? 1 : 0, by = pausedBy });
        }

        // Visitante: o host pausou ou mandou continuar.
        void OnlinePauseEv(bool on, string by)
        {
            if (on) { if (!paused) pausedAt = Time.realtimeSinceStartup; paused = true; pausedBy = by; resumeLeft = 0f; }
            else if (paused) { paused = false; resumeLeft = PauseResume; }
        }

        // A cada quadro, antes do passo da simulação. Verdadeiro: a corrida está parada (não chame sim.Step).
        bool PauseTick()
        {
            if (!CanPause) { paused = false; resumeLeft = 0f; return false; }
            float dt = Time.unscaledDeltaTime;
            if (paused)
            {
                if (OnlineRun && IsHost)
                {
                    if (Time.realtimeSinceStartup - pausedAt >= PauseLong) PauseResume_();
                    else if ((pauseBcastAcc += dt) >= 1f) PauseBroadcast();
                }
                return true;
            }
            if (resumeLeft > 0f) { resumeLeft -= dt; return true; }
            return false;
        }

        string PauseWho()
        {
            if (!OnlineRun || pausedBy == null) return "";
            var p = onlineChamp.pilots.Find(x => x.id == pausedBy);
            return p != null ? Safe(p.name) : OnlineHostName();
        }

        // O banner do meio enquanto pausado. Verdadeiro: a pausa está na tela.
        bool PauseUI()
        {
            bool show = Paused && CanPause;
            if (!show)
            {
                if (pauseShown) { pauseShown = false; pauseBox.style.display = DisplayStyle.None; DrawResult(resShown); }
                return false;
            }
            pauseShown = true;
            banner.style.display = DisplayStyle.Flex;
            banner.pickingMode = PickingMode.Position;
            DrawRecCard(false);
            resTable.style.display = resBtns.style.display = carPick.style.display = DisplayStyle.None;
            BannerColor(-1);
            bannerSub.style.display = DisplayStyle.Flex;
            if (!paused)
            {
                bannerTitle.text = $"Volta em {Mathf.CeilToInt(resumeLeft)}";
                bannerSub.text = "Prepare o dedo no acelerador.";
                pauseBox.style.display = DisplayStyle.None;
                return true;
            }
            bannerTitle.text = "Pausado";
            int left = Mathf.Max(0, Mathf.CeilToInt(PauseLong - (Time.realtimeSinceStartup - pausedAt)));
            bannerSub.text = !OnlineRun ? "P continua."
                : $"Por {PauseWho()} · continua quem pausou ou o host (P) · volta sozinha em {left} s.";
            bool mayGo = !OnlineRun || IsHost || pausedBy == myId;
            pauseBox.style.display = DisplayStyle.Flex;
            pauseGo.style.display = mayGo ? DisplayStyle.Flex : DisplayStyle.None;
            pauseGrid.style.display = OnlineRun ? DisplayStyle.None : DisplayStyle.Flex;
            return true;
        }
    }
}
