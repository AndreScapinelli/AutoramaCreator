using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Peças, moedas e garagem do carro (seção 78 do guia). As peças do construtor abrem pela carreira (tela Peças no
    // menu inicial, com as travas no construtor); as moedas são do save da carreira e compram a garagem do carro.
    public partial class RaceController
    {
        VisualElement shopLayer, shopList, coinChip, garLayer, garList;
        Label coinText, coinPop, garCoins, shopCoins;
        float coinShown, coinPopT, coinPulseT;
        const float CoinPopTime = 2.2f, CoinCountRate = 3f;
        Button shopAll;

        void OnEarned(int n)
        {
            coinPop.text = $"+{n}";
            coinPopT = CoinPopTime;
            coinPulseT = 0.5f;
        }

        int CoinsNow => Carteira.D.moedas;

        // A cada quadro: o selo só existe na carreira, fora da tela de saves; na prova só aparece com o "+N".
        void CoinTick()
        {
            if (coinChip == null) return;
            bool career = panel.CareerMode && kSave != null && kScreen != KScreen.Saves && !panel.MenuOpen;
            bool raceScreen = kScreen == KScreen.Race || kScreen == KScreen.Result;
            bool show = career && (!raceScreen || coinPopT > 0f);
            coinChip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!career) { coinShown = CoinsNow; coinPopT = 0f; return; }
            coinChip.style.top = raceScreen ? 52f : 12f;
            float target = CoinsNow, dt = Time.unscaledDeltaTime;
            if (coinShown != target) coinShown = Mathf.Abs(target - coinShown) < 1f ? target : Mathf.Lerp(coinShown, target, 1f - Mathf.Exp(-CoinCountRate * dt * 3f));
            string t = Mathf.RoundToInt(coinShown).ToString("N0", L.Cultura);
            if (coinText.text != t) coinText.text = t;
            if (coinPulseT > 0f) coinPulseT -= dt;
            float pulse = coinPulseT > 0f ? 1f + 0.18f * Mathf.Sin(coinPulseT / 0.5f * Mathf.PI) : 1f;
            coinChip.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
            if (coinPopT > 0f)
            {
                coinPopT -= dt;
                float u = 1f - coinPopT / CoinPopTime;          // 0 → 1
                coinPop.style.display = DisplayStyle.Flex;
                coinPop.style.translate = new Translate(0, -6f - 26f * u);
                coinPop.style.opacity = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
            }
            else coinPop.style.display = DisplayStyle.None;
        }

        void BuildShopUI()
        {
            // Peças (menu inicial): o que já abriu e em que prova abre o resto.
            shopLayer = El("k-full k-dim", panel.Root);
            shopLayer.style.display = DisplayStyle.None;
            var card = El("k-panel k-shop k-garage", shopLayer);
            var sh = El("k-shop-head", card);
            Text(L.T("PEÇAS E MESA"), "k-title", sh);
            shopCoins = Text("", "k-stars", sh);
            Text(L.T("Seu kit para o construtor, comprado com as moedas da carreira. Cada pista usa até as peças que você tem (montar outra pista não gasta) e cabe na sua mesa. Peça nova abre na prova da carreira que a apresenta. Correr em qualquer pista sempre pode."), "k-sub", card);
            var ss = new ScrollView(ScrollViewMode.Vertical);
            ss.AddToClassList("k-garage-scroll");
            card.Add(ss);
            shopList = ss.contentContainer;
            if (Progresso.Teste)
                shopAll = Btn("", () => { Progresso.P.tudo = !Progresso.P.tudo; Progresso.Save(); }, "btn start-btn", card);
            Btn(L.T("Fechar (Esc)"), () => ShowShop(false), "btn start-btn", card);

            // Garagem do carro (carreira).
            garLayer = El("k-full k-dim", panel.Root);
            garLayer.style.display = DisplayStyle.None;
            var gc = El("k-panel k-shop k-garage", garLayer);
            var head = El("k-shop-head", gc);
            Text(L.T("GARAGEM DO CARRO"), "k-title", head);
            garCoins = Text("", "k-stars", head);
            Text(L.T("Seu carro na carreira. Compre opções do ajuste e preparação com as moedas das provas. Fora da carreira o ajuste é livre e sem preparação."), "k-sub", gc);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("k-garage-scroll");
            gc.Add(scroll);
            garList = scroll.contentContainer;
            Btn(L.T("Fechar (G · Esc)"), () => ShowGarage(false), "btn start-btn", gc);

            // Selo das moedas (seção 74.1): canto de cima, à direita; anima ao ganhar. Clique abre a garagem.
            coinChip = El("coin-chip", panel.Root);
            coinChip.pickingMode = PickingMode.Position;
            coinChip.RegisterCallback<ClickEvent>(_ => { if (kScreen == KScreen.Map) ShowGarage(true); });
            coinChip.tooltip = L.T("Moedas da carreira · clique para abrir a garagem do carro");
            El("coin-disc", coinChip).Add(new Label("$") { pickingMode = PickingMode.Ignore });
            coinText = Text("0", "coin-text", coinChip);
            coinPop = Text("", "coin-pop", coinChip);
            coinPop.pickingMode = PickingMode.Ignore;

            panel.ShopRequested += () => ShowShop(true);
            Carteira.Changed += () => { if (ShopOpen) DrawShop(); };
            Progresso.Changed += OnProfileChanged;
            OnProfileChanged();
        }

        void ShowShop(bool on)
        {
            shopLayer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) DrawShop();
        }

        bool ShopOpen => shopLayer != null && shopLayer.style.display == DisplayStyle.Flex;
        bool GarageOpen => garLayer != null && garLayer.style.display == DisplayStyle.Flex;

        void ShopKeys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (ShopOpen && kb.escapeKey.wasPressedThisFrame) ShowShop(false);
        }

        void DrawShop()
        {
            shopList.Clear();
            shopCoins.text = $"◎ {Carteira.D.moedas.ToString("N0", L.Cultura)}";
            if (Progresso.All) Text(L.T("Desbloquear tudo ligado: mesa sem limite e peças de graça (teste)."), "k-shop-own", shopList);

            // Mesa (seção 94): 5 tamanhos, um depois do outro.
            Text(L.T("MESA"), "k-gar-head", shopList);
            for (int i = 0; i < Carteira.Mesas.Length; i++)
            {
                int m = i;
                var me = Carteira.Mesas[i];
                var row = El("k-shop-row", shopList);
                var texts = El("k-shop-texts", row);
                Text(L.T(me.nome), "k-save-title", texts);
                Text(L.F("pista de até {0} × {0} células", me.celulas), "k-sub", texts);
                if (i <= Carteira.D.mesa) { Text(i == Carteira.D.mesa ? L.T("✓ a sua") : L.T("✓"), "k-shop-own", row); continue; }
                string why = Carteira.CanBuyMesa(i);
                var b = Btn(L.F("Comprar · ◎ {0}", me.preco.ToString("N0", L.Cultura)), () => { if (Carteira.BuyMesa(m)) Progresso.ApplyGate(); }, "btn", row);
                b.SetEnabled(why == "");
                if (why != "") Text(why, "k-shop-why", row);
            }
        }

        // Perfil ou carreira mudou ("Desbloquear tudo", peça aberta): travas, botões e telas.
        void OnProfileChanged()
        {
            if (!kBuilding) { Progresso.ApplyGate(); TrackBuilder.LockTip = e => Progresso.LockText(e.def.Id); }
            panel.RefreshAllowed();
            if (shopAll != null)
            {
                shopAll.text = Progresso.P.tudo ? L.T("Desbloquear tudo: LIGADO (teste, até o jogo sair)") : L.T("Desbloquear tudo: desligado (teste, até o jogo sair)");
                shopAll.EnableInClassList("btn--on", Progresso.P.tudo);
            }
            if (ShopOpen) DrawShop();
            if (GarageOpen) DrawGarage();
            if (kStarsLabel != null && kScreen == KScreen.Map) DrawMapUI();
        }

        // ---------- garagem do carro ----------

        void ShowGarage(bool on)
        {
            if (on && (kSave == null || kScreen != KScreen.Map)) return;
            garLayer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) DrawGarage();
            else if (kScreen == KScreen.Map) DrawMapUI();
        }

        void GarageSave() { Career.Save(kSlot, kSave); DrawGarage(); }

        // Cada peça: as três opções (comprada seleciona; não comprada mostra o preço) e, embaixo, a trilha de níveis da
        // opção escolhida.
        void DrawGarage()
        {
            var g = Carteira.D.garagem;
            CarShop.Fix(g);
            garCoins.text = $"◎ {Carteira.D.moedas}";
            garList.Clear();
            Text(L.F("Níveis à venda até o {0} (Garagem 3 · Clube 6 · Regional 8 · Nacional 10). O nível n custa {1} × n.", CarShop.Cap(kSave), CarShop.LevelStep), "k-sub", garList);
            foreach (var peca in CarShop.Pecas)
            {
                string pc = peca;
                var names = CarShop.Names(pc);
                int cur = CarShop.Get(g.carro, pc);
                var row = El("segmented car-row", garList);
                Text(CarShop.Title(pc), "seg-label", row);
                foreach (int o in new[] { 1, 0, 2 })
                {
                    int oo = o;
                    if (CarShop.HasOpt(g, pc, o))
                    {
                        int lvo = CarShop.Level(g, pc, o);
                        Btn(lvo > 0 ? $"{names[o]} · {lvo}" : names[o], () => { g.carro = CarShop.With(g.carro, pc, oo); GarageSave(); }, cur == o ? "btn btn--on" : "btn", row);
                    }
                    else
                    {
                        string why = CarShop.CanBuyOpt(kSave, pc, o);
                        var b = Btn($"{names[o]} ◎{CarShop.OptPrice(pc)}", () =>
                        {
                            if (!CarShop.BuyOpt(kSave, pc, oo)) return;
                            g.carro = CarShop.With(g.carro, pc, oo);
                            panel.Toast(L.F("{0} {1}: é seu!", CarShop.Title(pc), names[oo].ToLowerInvariant()));
                            GarageSave();
                        }, "btn k-gar-buy", row);
                        b.SetEnabled(why == "");
                        b.tooltip = why == "" ? L.T("Comprar") : why;
                    }
                }
                if (pc == "bolha") ModelRow(garList, g.carro, v => { g.carro.modelo = v.modelo; GarageSave(); });
                // Trilha da opção escolhida.
                int lv = CarShop.Level(g, pc, cur);
                var lr = El("k-shop-row k-gar-lv", garList);
                var texts = El("k-shop-texts", lr);
                Text($"{names[cur]}  {new string('●', lv)}{new string('○', CarShop.MaxNivel - lv)}  {lv}/{CarShop.MaxNivel}", "k-gar-track", texts);
                Text(CarShop.LevelText(pc, cur), "k-sub", texts);
                if (lv >= CarShop.MaxNivel) { Text(L.T("✓ máximo"), "k-shop-own", lr); continue; }
                string whyL = CarShop.CanBuyLevel(kSave, pc, cur);
                var bl = Btn(L.F("Nível {0} · ◎ {1}", lv + 1, CarShop.LevelPrice(lv + 1)), () =>
                {
                    if (!CarShop.BuyLevel(kSave, pc, cur)) return;
                    GarageSave();
                }, "btn btn--primary", lr);
                bl.SetEnabled(whyL == "");
                if (whyL != "") Text(whyL, "k-shop-why", lr);
            }
            Text(L.T("COMO FICA"), "k-gar-head", garList);
            DrawCarCard(garList, CarShop.RaceCar(kSave, null), null, false, false);
        }

        // ---------- moedas da carreira ----------

        // Prêmio de um nó (Career.StarPrize e companhia, seção 78), somado às moedas do save.
        int CareerCoins(NodeDef n, int before, int after, int beaten)
        {
            int c = 0;
            for (int s = before; s < after && s < Career.StarPrize.Length; s++) c += Career.StarPrize[s];
            if (n.tipo == "rival" && before == 0 && after > 0) c += Career.RivalPrize;
            if (n.tipo == "campeonato" && before < 2 && after >= 2 && Career.IsFinal(KRegion, n)) c += Career.ChampPrize[Mathf.Clamp(kRegion, 0, Career.ChampPrize.Length - 1)];
            // Prêmio do campeonato pela posição na tabela (seção 92), toda vez; metade na sequência da mesma prova, como a corrida.
            kPurse = n.tipo == "campeonato" && kRes.terminou ? Career.Purse(n, kRes.posicao) : 0;
            if (kSave.ultimoNo == n.id) kSave.seguidas++; else { kSave.ultimoNo = n.id; kSave.seguidas = 1; }
            float race = Career.RacePrize + Career.PlacePrize * Mathf.Max(0, beaten);
            if (kSave.seguidas > Career.Streak) { race *= 0.5f; kPurse /= 2; }
            c += kPurse;
            c += Mathf.RoundToInt(race);
            Carteira.D.moedas += c;
            if (c > 0) OnEarned(c);
            return c;
        }
    }
}
