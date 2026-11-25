using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Tutorial da primeira vez (seção 102 do guia): monta uma pista peça por peça (só retas e curvas, mesa de 35
    // células, de graça), fecha com o Q, mostra o custo e salva; depois corre nela, um controle por vez: câmeras 1–4,
    // acelerar, sair na curva e recolocar, meia aceleração, ajuste fino da meia, turbo e 3 voltas.
    // Cada passo tem texto, uma condição para seguir e, se "hold", para a corrida e espera o Continuar (Enter).
    public partial class RaceController
    {
        class TutStep
        {
            public Func<string> text;
            public Func<bool> done, skip;
            public Action enter;
            public bool hold;            // corrida parada, botão Continuar
            public Action tick;          // a cada quadro, antes da condição
            public bool world, tiles;    // clique na mesa · em qualquer cartão aberto da paleta (além do destaque)
        }

        const int TutMesa = 35;
        const string TutBackupKey = "autorama.tutorial.corrida";     // ajustes da corrida de antes do tutorial

        static bool TutPiece(PaletteEntry e) => e.rise == 0 && (e.def == PieceCatalog.Short || e.def == PieceCatalog.Long || e.def == PieceCatalog.Curve || e.def == PieceCatalog.Half);
        static bool TutStraight(PaletteEntry e) => e.rise == 0 && (e.def == PieceCatalog.Short || e.def == PieceCatalog.Long);
        static bool TutCurve(PaletteEntry e) => e.def == PieceCatalog.Curve || e.def == PieceCatalog.Half;
        static bool TutId(string id) => id == PieceCatalog.Short.Id || id == PieceCatalog.Long.Id || id == PieceCatalog.Curve.Id || id == PieceCatalog.Half.Id;

        List<TutStep> tutSteps;
        int tutI = -1;
        bool tutRace;                      // fase da corrida (senão, construção)
        bool TutOn => tutI >= 0;
        bool TutHold => TutOn && tutSteps[tutI].hold;
        VisualElement tutCard, tutAsk;
        Label tutCount, tutText, tutWarn;
        Button tutNext;
        float tutT;                        // segundos no passo
        TrackSave tutStash;                // a pista do construtor antes do tutorial (volta se sair no meio)
        bool tutSaved, tutNextPressed;
        float tutPanT; int tutZoomIn, tutZoomOut;
        int tutMark, tutOffs, tutLaps, tutHalf;
        bool tutClean;
        float tutBoostT;
        TrackCloser.Job tutJob;            // o Q de teste, em segundo plano, a cada peça
        bool tutCanClose = true;

        // ---------- Montagem e primeira abertura ----------

        void BuildTutorialUI()
        {
            tutCard = El("tut-card", panel.Root);
            tutCard.style.display = DisplayStyle.None;
            tutCount = Text("", "tut-count", tutCard);
            tutText = Text("", "tut-text", tutCard);
            tutWarn = Text("", "tut-warn", tutCard);
            var row = El("row", tutCard);
            Btn(L.T("Sair do tutorial"), () => TutQuit(), "btn", row);
            tutNext = Btn(L.T("Continuar  (Enter)"), () => tutNextPressed = true, "btn btn--primary", row);
            panel.TrackSaved += s => { if (TutOn && !tutRace) tutSaved = true; };

            // Fechou o jogo no meio da corrida do tutorial: os ajustes da corrida voltam.
            string bak = PlayerPrefs.GetString(TutBackupKey, "");
            if (bak != "") { TutRestoreSettings(bak); }

            var P = Progresso.P;
            if (P.tutorial == 0 && TutVeteran()) { P.tutorial = 3; Progresso.Save(); }
            if (P.tutorial == 0 || P.tutorial == 1) TutAsk(P.tutorial == 1);
        }

        // Quem já tem pista salva ou carreira começada não é novato: não pergunta (Opções tem o "Refazer tutorial").
        static bool TutVeteran()
        {
            if (TrackLibrary.List().Count > 0) return true;
            try { return System.IO.File.Exists(System.IO.Path.Combine(TrackLibrary.Root, "carreira-1.json")); } catch { return false; }
        }

        void TutAsk(bool race)
        {
            tutAsk = El("tut-ask", panel.Root);
            var card = El("tut-ask-card", tutAsk);
            Text(race ? L.T("Continuar o tutorial?") : L.T("Primeira vez aqui?"), "k-title", card);
            Text(race ? L.T("Sua pista está pronta. Falta aprender a correr nela.") : L.T("Em uns 5 minutos você monta a sua primeira pista, peça por peça, e aprende a correr nela."), "k-sub", card);
            var row = El("row", card);
            Btn(L.T("Pular"), () => { CloseAsk(); Progresso.P.tutorial = 3; Progresso.Save(); }, "btn", row);
            Btn(race ? L.T("Continuar") : L.T("Começar o tutorial"), () => { CloseAsk(); if (race) TutStartRace(true); else TutStart(); }, "btn btn--primary", row);
            tutAsk.BringToFront();
        }

        void CloseAsk() { tutAsk?.RemoveFromHierarchy(); tutAsk = null; }

        // Opções: "Refazer tutorial".
        void TutRedo()
        {
            if (Racing || OnlineRun) return;
            CloseKeys();
            Progresso.P.tutorial = 0; Progresso.Save();
            TutStart();
        }

        // ---------- Construção ----------

        void TutStart()
        {
            CloseAsk();
            panel.ShowMenu(false);
            panel.SetMode(BuilderPanel.Mode.Build);
            tutStash = builder.ToSave();
            TrackBuilder.AllowedEntry = TutPiece;
            TrackBuilder.MaxPieces = 0;
            TrackBuilder.RuleNotice = L.N("No tutorial, só retas e curvas. As outras peças abrem na carreira.");
            TrackBuilder.GapOk = _ => false;
            TrackBuilder.RiseOk = () => false;
            TrackBuilder.BoardLimit = TutMesa;
            TrackBuilder.SaveGate = null;          // de graça: o custo aparece no fim
            TrackBuilder.LockTip = _ => L.T("No tutorial, só retas e curvas.");
            TrackCloser.Allowed = TutId;
            builder.NewTrack(2);
            panel.RefreshAllowed();
            panel.FrameTrack();
            builder.Changed += TutTrackChanged;
            tutRace = false;
            tutSteps = TutBuildSteps();
            TutGo(0);
        }

        List<TutStep> TutBuildSteps() => new List<TutStep>
        {
            new TutStep { hold = true, text = () => L.T("<b>Bem-vindo ao Autorama Mania!</b>\nVamos montar a sua primeira pista, peça por peça, e depois correr nela.") },
            new TutStep { text = () => L.T("<b>Mover a mesa.</b> Segure o <b>botão direito</b> (ou o do meio) do mouse e arraste."), done = () => tutPanT > 0.6f },
            new TutStep { text = () => L.T("<b>Aproximar.</b> Gire a <b>roda do mouse</b> para a frente."), done = () => tutZoomIn > 2 },
            new TutStep { text = () => L.T("<b>Afastar.</b> Gire a <b>roda do mouse</b> para trás.\nSe a pista sumir da tela, <b>C</b> enquadra de novo."), done = () => tutZoomOut > 2 },
            new TutStep { enter = () => panel.Highlight3D(true), tick = () => { if (!cam.Tilted) tutPanT = 0f; }, text = () => L.T("<b>Vista 3D.</b> Aperte <b>V</b> (ou o botão <b>3D</b>) para inclinar a câmera.\nNa vista 3D, o botão direito <b>gira</b> a mesa em vez de mover. Experimente."),
                done = () => cam.Tilted && tutPanT > 0.6f },
            new TutStep { text = () => L.T("<b>De volta para cima.</b> Aperte <b>V</b> (ou o botão <b>3D</b>) de novo para a vista de cima, a melhor para montar."),
                done = () => !cam.Tilted },
            new TutStep { hold = true, enter = () => { panel.Highlight3D(false); panel.FrameTrack(); }, text = () => L.T("<b>A largada</b> já está na mesa: a pista começa e termina nela.\nA seta mostra a ponta onde a próxima peça encaixa.") },
            new TutStep { enter = () => { tutMark = Count(TutStraight); panel.HighlightTiles(TutStraight); },
                text = () => L.T("<b>Uma reta.</b> Clique em <b>Reta comprida</b> na paleta (ou aperte <b>2</b>). Ela encaixa sozinha na ponta."), done = () => Count(TutStraight) > tutMark },
            new TutStep { enter = () => { tutMark = Count(TutCurve); panel.HighlightTiles(TutCurve); },
                world = true, text = () => L.T("<b>Uma curva.</b> Escolha uma <b>Curva longa</b>: <b>3</b> vira à esquerda, <b>4</b> à direita.\nSe virou para o lado errado, clique nela e aperte <b>F</b>."), done = () => Count(TutCurve) > tutMark },
            new TutStep { enter = () => { tutMark = Count(TutStraight); panel.HighlightTiles(TutStraight); },
                text = () => L.T("Mais uma <b>reta</b>: <b>1</b> é a curta, <b>2</b> a comprida."), done = () => Count(TutStraight) > tutMark },
            new TutStep { enter = () => { panel.HighlightTiles(null); panel.HighlightUndo(true); },
                text = () => L.T("<b>Errou?</b> <b>Ctrl+Z</b> (ou o botão <b>Desfazer</b>) desfaz a última ação. Experimente: a última reta some."), done = () => builder.CanRedo },
            new TutStep { enter = () => { panel.HighlightUndo(false); panel.HighlightRedo(true); },
                text = () => L.T("<b>Refazer.</b> Mudou de ideia? <b>Ctrl+Y</b> (ou o botão <b>Refazer</b>) traz de volta o que foi desfeito."), done = () => !builder.CanRedo },
            new TutStep { world = true, enter = () => { panel.HighlightRedo(false); panel.HighlightRemove(true); tutMark = builder.pieces.Count; },
                text = () => L.T("<b>Apagar uma peça.</b> Clique numa peça da pista para selecionar e aperte <b>Delete</b> (ou o botão da <b>lixeira</b>).\n<b>Backspace</b> apaga a última peça. A largada não sai."),
                done = () => builder.pieces.Count < tutMark },
            new TutStep { world = true, tiles = true, enter = () => { panel.HighlightRemove(false); if (builder.Selected >= 0) builder.Select(builder.Selected); tutMark = builder.pieces.Count; },
                text = () => L.F("<b>Agora é com você:</b> ponha mais 3 peças, retas ou curvas, do jeito que quiser ({0} de 3).", Mathf.Clamp(builder.pieces.Count - tutMark, 0, 3)),
                done = () => builder.pieces.Count - tutMark >= 3 || builder.Info.closed },
            new TutStep { enter = () => panel.HighlightClose(true), skip = () => builder.Info.closed,
                text = () => L.T("<b>Fechar sozinho.</b> Aperte <b>Q</b> (ou o botão <b>Fechar circuito</b>) e o jogo completa a pista até a largada.\nSe não achar, desfaça uma peça (Ctrl+Z) e tente de novo."),
                done = () => builder.Info.closed && builder.Info.overlaps.Count == 0 && !builder.Closing },
            new TutStep { enter = () => { panel.HighlightClose(false); panel.HighlightSave(true); tutSaved = false; },
                text = () => L.F("<b>Pista fechada!</b> Esta pista custaria <b>{0}</b>. No tutorial ela sai de graça; depois, as moedas vêm sobretudo da carreira.\nDê um <b>nome</b> à pista no painel (MINHAS PISTAS) e salve (<b>Ctrl+S</b>).", PiecePrice.Money(PiecePrice.Total(builder.ToSave()))),
                done = () => tutSaved },
        };

        int Count(Func<PaletteEntry, bool> f)
        {
            int n = 0;
            foreach (var p in builder.pieces)
                if (f(new PaletteEntry(p.Def, p.dir, p.rise, Key.None, "", "", ""))) n++;
            return n;
        }

        // A cada peça (passos de montar): o Q de teste começa de novo, em segundo plano.
        void TutTrackChanged()
        {
            tutJob?.Cancel(); tutJob = null;
            tutCanClose = true;
            if (!TutOn || tutRace || builder.Info == null || builder.Info.closed || builder.pieces.Count < 2) return;
            int fe = builder.FrontEnd;
            tutJob = TrackCloser.Start(builder.Origin, builder.pieces.GetRange(0, fe), builder.pieces.GetRange(fe, builder.Tail), false, builder.StartZ);
        }

        void TutBuildEnd()
        {
            builder.Changed -= TutTrackChanged;
            tutJob?.Cancel(); tutJob = null;
            panel.HighlightTiles(null);
            panel.HighlightClose(false);
            OnProfileChanged();                    // voltam a mesa, o preço e as travas de sempre
        }

        // ---------- Corrida ----------

        void TutStartRace(bool resume)
        {
            CloseAsk();
            panel.ShowMenu(false);
            var P = Progresso.P;
            if (resume)                            // voltou depois de fechar o jogo: abre a pista do tutorial
            {
                panel.SetMode(BuilderPanel.Mode.Build);
                tutStash = builder.ToSave();
                try { if (!string.IsNullOrEmpty(P.tutPista)) builder.Load(TrackSave.FromJson(P.tutPista)); } catch { }
            }
            if (builder.Info == null || !builder.Info.closed) { TutQuit(); panel.Toast(L.T("A pista do tutorial não está fechada.")); return; }
            P.tutorial = 1; P.tutPista = builder.ToSave().ToJson(false); Progresso.Save();
            PlayerPrefs.SetString(TutBackupKey, JsonUtility.ToJson(set)); PlayerPrefs.Save();
            set.track = "builder"; set.raceFaixas = 0; set.cars = 1;
            set.slots[0].ai = false; set.slots[0].carro = default;
            set.turbo = true; set.ghost = false; set.byTime = false; set.laps = 99;
            tutRace = true;
            tutSteps = TutRaceSteps();
            panel.SetMode(BuilderPanel.Mode.Race);
            TutGo(0);
        }

        CarState TutCar => sim.cars[0];

        // Volta completa desde o passo: limpa (sem sair da pista) ou não.
        bool TutLap()
        {
            var c = TutCar;
            if (c.laps <= tutLaps) return false;
            tutClean = c.offs == tutOffs;
            tutLaps = c.laps; tutOffs = c.offs;
            return true;
        }
        void TutLapMark() { tutLaps = TutCar.laps; tutOffs = TutCar.offs; tutClean = false; }

        List<TutStep> TutRaceSteps()
        {
            string gas = KeyText(GasKeyAt(0)), half = KeyText(HalfKeyAt(0)), reslot = KeyText(ReslotKeyAt(0)), turbo = KeyText(TurboKeyAt(0));
            string minus = KeyText(Controles.Get(0, Controles.Acao.MeiaMenos)), plus = KeyText(Controles.Get(0, Controles.Acao.MeiaMais));
            var list = new List<TutStep>
            {
                new TutStep { hold = true, text = () => L.T("<b>Hora de correr!</b> O carrinho vermelho é o seu.\nAntes da largada, conheça as câmeras.") },
            };
            for (int i = 0; i < 4; i++)
            {
                int m = i;
                list.Add(new TutStep { text = () => L.F("Aperte <b>{0}</b>: <b>{1}</b>. {2}.", m + 1, CamModes[m], CamModeHints[m]), done = () => set.cameraMode == (int)CamModeValues[m] && tutT > 0.8f });
            }
            list.AddRange(new[]
            {
                new TutStep { text = () => L.T("Escolha a câmera de que mais gostou (<b>1</b> a <b>4</b>): ela fica como a sua.\nDepois aperte <b>Enter</b> para largar."),
                    done = () => tutNextPressed, enter = () => tutNext.text = L.T("Largar  (Enter)") },
                new TutStep { enter = StartRace,
                    text = () => L.F("Espere o semáforo e <b>segure {0}</b> para acelerar. Soltou, o carrinho freia.", gas),
                    done = () => sim.phase == RaceSim.Phase.Running && (TutCar.v > 0.5f * sim.set.vmax * sim.Speed || TutCar.mode != CarMode.OnSlot) },
                new TutStep { enter = TutLapMark, text = () => L.F("Continue com o <b>{0}</b> no fundo, até na curva, para ver o que acontece…", gas),
                    done = () => TutCar.offs > tutOffs || TutLap() },
                new TutStep { hold = true, skip = () => TutCar.mode == CarMode.OnSlot,
                    text = () => L.F("<b>Saiu da pista!</b> Rápido demais na curva, a traseira escorrega, a barra de derrapagem enche e o carrinho voa.\nAperte <b>{0}</b> para recolocar no trilho.", reslot) },
                new TutStep { skip = () => TutCar.mode == CarMode.OnSlot, text = () => L.F("Aperte <b>{0}</b> para recolocar o carrinho.", reslot), done = () => TutCar.mode == CarMode.OnSlot },
                new TutStep { enter = TutLapMark,
                    text = () => L.F("<b>A meia aceleração.</b> Nas retas, <b>{0}</b>. Chegando na curva, troque para o <b>{1}</b>: o acelerador vai só até {2}% e o carrinho faz a curva sem sair.\nDê <b>uma volta sem sair da pista</b>.", gas, half, HalfOf(0)),
                    done = () => TutLap() && tutClean },
                new TutStep { enter = () => { TutLapMark(); tutHalf = HalfOf(0); },
                    text = () => L.F("<b>Ajuste fino.</b> <b>{0}</b> e <b>{1}</b> mudam até onde o <b>{2}</b> acelera (agora <b>{3}%</b>). Mais alto, a curva fica mais rápida, mas é mais fácil sair.\nMude o valor e dê uma volta limpa.", minus, plus, half, HalfOf(0)),
                    done = () => TutLap() && tutClean && HalfOf(0) != tutHalf },
                new TutStep { hold = true, enter = () => TutCar.turbo = 1f,
                    text = () => L.F("<b>O turbo.</b> A barra do turbo enche a cada volta (e um pouco a cada saída). Deixei o tanque cheio para você.\nNa reta, segure <b>{0}</b>: o carrinho passa da velocidade máxima até o tanque acabar.", turbo) },
                new TutStep { enter = () => { tutBoostT = 0f; TutCar.turbo = Mathf.Max(TutCar.turbo, 1f); },
                    text = () => L.F("Segure <b>{0}</b> numa reta.", turbo), done = () => tutBoostT > 0.5f },
                new TutStep { hold = true, text = () => L.T("<b>Agora pra valer: 3 voltas.</b> Use tudo: acelerar nas retas, meia nas curvas e turbo quando o tanque encher.") },
                new TutStep { enter = () => { set.laps = 3; StartRace(); },
                    text = () => L.F("Complete <b>3 voltas</b> ({0} de 3).", Mathf.Clamp(TutCar.laps, 0, 3)),
                    done = () => sim.phase == RaceSim.Phase.Done || TutCar.finish != null },
                new TutStep { hold = true, enter = () => tutNext.text = L.T("Terminar"),
                    text = () => L.T("<b>Parabéns!</b> Você montou a sua pista e completou 3 voltas.\nA <b>carreira</b> abre as outras peças e paga as moedas. Todas as teclas ficam em <b>Opções</b>, no menu inicial.") },
            });
            return list;
        }

        void TutRestoreSettings(string json)
        {
            int cam = set.cameraMode;           // a câmera escolhida no tutorial fica
            try { JsonUtility.FromJsonOverwrite(json, set); } catch { }
            if (tutRace) set.cameraMode = cam;
            set.FixSlots();
            set.Save();
            PlayerPrefs.DeleteKey(TutBackupKey); PlayerPrefs.Save();
        }

        // ---------- Quadro a quadro ----------

        void TutGo(int i)
        {
            while (i < tutSteps.Count && tutSteps[i].skip != null && tutSteps[i].skip()) i++;
            if (i >= tutSteps.Count) { TutPhaseDone(); return; }
            tutI = i; tutT = 0f; tutNextPressed = false; tutPanT = 0f; tutZoomIn = tutZoomOut = 0;
            tutNext.text = L.T("Continuar  (Enter)");
            tutSteps[i].enter?.Invoke();
            var st = tutSteps[i];
            BuilderPanel.ClickGate = el => TutClickOk(el, st);
            BuilderPanel.WorldGate = () => st.world;
            tutCard.style.display = DisplayStyle.Flex;
            tutCard.BringToFront();
            TutDraw();
        }

        void TutDraw()
        {
            var s = tutSteps[tutI];
            tutCount.text = (tutRace ? L.T("TUTORIAL · CORRIDA") : L.T("TUTORIAL · CONSTRUÇÃO")) + L.F("  ·  {0} de {1}", tutI + 1, tutSteps.Count);
            tutText.text = s.text();
            bool warn = !tutRace && !tutCanClose && !builder.Info.closed;
            tutWarn.text = warn ? L.T("Por aqui o Q não consegue fechar a pista. Desfaça (Ctrl+Z) e tente outra direção.") : "";
            tutWarn.style.display = warn ? DisplayStyle.Flex : DisplayStyle.None;
            tutNext.style.display = s.hold || tutI == TutChooseStep ? DisplayStyle.Flex : DisplayStyle.None;
        }

        int TutChooseStep => tutRace ? 5 : -1;     // escolher a câmera e largar: o Enter larga

        void TutTick()
        {
            if (!TutOn) return;
            // Saiu do tutorial por outro caminho (menu inicial, outra aba).
            var want = tutRace ? BuilderPanel.Mode.Race : BuilderPanel.Mode.Build;
            if (panel.CurrentMode != want || panel.MenuOpen) { TutQuit(); return; }
            tutT += Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var s = tutSteps[tutI];
            if (kb != null && !IsTyping && (s.hold || tutI == TutChooseStep) && tutT > 0.3f && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) tutNextPressed = true;
            var ms = Mouse.current;
            if (ms != null)
            {
                if ((ms.rightButton.isPressed || ms.middleButton.isPressed) && ms.delta.ReadValue().sqrMagnitude > 1f) tutPanT += Time.unscaledDeltaTime;
                float sc = ms.scroll.ReadValue().y;
                if (sc > 0f) tutZoomIn++; else if (sc < 0f) tutZoomOut++;
            }
            if (tutRace && TutCar.boost && sim.phase == RaceSim.Phase.Running) tutBoostT += Time.deltaTime;
            if (tutJob != null && !builder.Closing && !tutJob.Pump(4))
            {
                tutCanClose = tutJob.Result.ok;
                tutJob = null;
            }
            s.tick?.Invoke();
            bool ok = s.hold ? tutNextPressed : s.done != null && s.done();
            if (ok) TutGo(tutI + 1); else TutDraw();
        }

        void TutPhaseDone()
        {
            if (!tutRace)
            {
                // A pista do tutorial fica paga (de graça): mexer nela depois só cobra o que passar disso.
                var save = builder.ToSave();
                Carteira.Dar(TrackLibrary.Key(save.nome), PiecePrice.Total(save));
                TutBuildEnd();
                tutI = -1;
                TutStartRace(false);
                return;
            }
            TutRestoreSettings(PlayerPrefs.GetString(TutBackupKey, JsonUtility.ToJson(set)));
            Progresso.P.tutorial = 2; Progresso.P.tutPista = ""; Progresso.Save();
            TutClose();
            panel.ShowMenu(true);
        }

        // Sair no meio: as travas e os ajustes voltam; a pista de antes volta ao construtor. Não pergunta mais
        // (Opções tem o "Refazer tutorial").
        void TutQuit()
        {
            if (!TutOn && tutAsk == null) return;
            CloseAsk();
            bool race = tutRace;
            if (!race) TutBuildEnd();
            else TutRestoreSettings(PlayerPrefs.GetString(TutBackupKey, JsonUtility.ToJson(set)));
            if (Progresso.P.tutorial < 2) { Progresso.P.tutorial = 3; Progresso.Save(); }
            TutClose();
            if (!race && tutStash != null && panel.CurrentMode == BuilderPanel.Mode.Build) builder.Load(tutStash);
            tutStash = null;
        }

        static bool TutClickOk(VisualElement el, TutStep s)
        {
            for (var e = el; e != null; e = e.parent)
            {
                if (e.ClassListContains("tut-card") || e.ClassListContains("tut-ask") || e.ClassListContains("tut-hi") || e.ClassListContains("pal-tab")) return true;
                if (s.tiles && e.ClassListContains("tile") && e.enabledInHierarchy) return true;
            }
            return false;
        }

        void TutClose()
        {
            BuilderPanel.ClickGate = null; BuilderPanel.WorldGate = null;
            panel.HighlightUndo(false); panel.HighlightSave(false); panel.Highlight3D(false); panel.HighlightRedo(false); panel.HighlightRemove(false);
            tutI = -1; tutRace = false;
            tutCard.style.display = DisplayStyle.None;
        }
    }
}
