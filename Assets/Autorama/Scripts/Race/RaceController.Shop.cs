using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Moedas e Loja (seção 74 do guia): a tela da Loja, as travas no construtor e as moedas de cada corrida.
    public partial class RaceController
    {
        VisualElement shopLayer, shopList, coinChip;
        Label coinText, coinPop;
        float coinShown, coinPopT, coinPulseT;
        const float CoinPopTime = 2.2f, CoinCountRate = 3f;

        void OnEarned(int n)
        {
            coinPop.text = $"+{n}";
            coinPopT = CoinPopTime;
            coinPulseT = 0.5f;
        }

        // A cada quadro: quando aparece, conta até o total e anima o "+N".
        void CoinTick()
        {
            if (coinChip == null) return;
            // Nas telas de corrida (aba Corrida, Campeonato, Online, a prova da carreira) só aparece com o "+N", abaixo do menu da câmera.
            bool raceScreen = panel.RaceMode && RaceUI && !ShopOpen && !panel.MenuOpen;
            bool show = !raceScreen || coinPopT > 0f;
            coinChip.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            coinChip.style.top = raceScreen ? 52f : 12f;
            float target = Progresso.P.moedas, dt = Time.unscaledDeltaTime;
            if (coinShown != target) coinShown = Mathf.Abs(target - coinShown) < 1f ? target : Mathf.Lerp(coinShown, target, 1f - Mathf.Exp(-CoinCountRate * dt * 3f));
            string t = Mathf.RoundToInt(coinShown).ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
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
        Label shopCoins;
        Button shopAll;

        void BuildShopUI()
        {
            shopLayer = El("k-full k-dim", panel.Root);
            shopLayer.style.display = DisplayStyle.None;
            var card = El("k-panel k-shop", shopLayer);
            var head = El("k-shop-head", card);
            Text("LOJA", "k-title", head);
            shopCoins = Text("", "k-stars", head);
            Text("Peças novas para o construtor. Ganhe moedas correndo; a carreira abre cada peça para compra. Correr numa pista com peça que você não tem sempre pode — só não dá para colocar a peça.", "k-sub", card);
            shopList = El(null, card);
            if (Progresso.Teste)
            {
                shopAll = Btn("", () => { Progresso.P.tudo = !Progresso.P.tudo; Progresso.Save(); }, "btn start-btn", card);
            }
            Btn("Fechar (Esc)", () => ShowShop(false), "btn start-btn", card);

            // Selo das moedas (seção 74.1): canto de cima, à direita; anima ao ganhar.
            coinChip = El("coin-chip", panel.Root);
            coinChip.pickingMode = PickingMode.Position;
            coinChip.RegisterCallback<ClickEvent>(_ => { if (!Racing) ShowShop(true); });
            coinChip.tooltip = "Moedas · clique para abrir a Loja";
            El("coin-disc", coinChip).Add(new Label("$") { pickingMode = PickingMode.Ignore });
            coinText = Text("0", "coin-text", coinChip);
            coinPop = Text("", "coin-pop", coinChip);
            coinPop.pickingMode = PickingMode.Ignore;
            coinShown = Progresso.P.moedas;

            panel.ShopRequested += () => ShowShop(true);
            Progresso.Changed += OnProfileChanged;
            Progresso.Earned += OnEarned;
            OnProfileChanged();
        }

        void ShowShop(bool on)
        {
            shopLayer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (on) DrawShop();
        }

        bool ShopOpen => shopLayer != null && shopLayer.style.display == DisplayStyle.Flex;

        void ShopKeys()
        {
            var kb = Keyboard.current;
            if (ShopOpen && kb != null && kb.escapeKey.wasPressedThisFrame) ShowShop(false);
        }

        void DrawShop()
        {
            shopCoins.text = $"◎ {Progresso.P.moedas}";
            shopList.Clear();
            foreach (var it in Progresso.Loja)
            {
                var item = it;
                var row = El("k-shop-row", shopList);
                var texts = El("k-shop-texts", row);
                Text(it.nome, "k-save-title", texts);
                Text(it.pecas, "k-sub", texts);
                string why = Progresso.CanBuy(it);
                if (Progresso.Owns(it.id)) Text(Progresso.All && !Progresso.P.compras.Contains(it.id) ? "liberada (teste)" : "✓ sua", "k-shop-own", row);
                else
                {
                    var b = Btn($"◎ {it.preco}", () => { if (Progresso.Buy(item)) panel.Toast($"{item.nome}: é sua!"); }, "btn btn--primary", row);
                    b.SetEnabled(why == "");
                    if (why != "") Text(why, "k-shop-why", row);
                }
            }
        }

        // Perfil mudou (compra, moedas, "Desbloquear tudo"): travas, botões e telas.
        void OnProfileChanged()
        {
            if (!kBuilding) { Progresso.ApplyGate(); TrackBuilder.LockTip = e => Progresso.LockText(e.def.Id); }
            panel.RefreshAllowed();
            panel.SetShopCoins(Progresso.P.moedas);
            if (shopAll != null)
            {
                shopAll.text = Progresso.P.tudo ? "Desbloquear tudo: LIGADO (teste, até o jogo sair)" : "Desbloquear tudo: desligado (teste, até o jogo sair)";
                shopAll.EnableInClassList("btn--on", Progresso.P.tudo);
            }
            if (ShopOpen) DrawShop();
            if (kStarsLabel != null && kScreen == KScreen.Map) DrawMapUI();
        }

        RaceSim.Phase coinPhase;
        // Chamado em todo quadro (offline e online): a passagem para Done paga a corrida.
        void CoinsTick()
        {
            if (sim == null || sim.phase == coinPhase) return;
            coinPhase = sim.phase;
            if (coinPhase == RaceSim.Phase.Done) RaceCoins();
        }

        // Fim de uma corrida fora da carreira (livre, campeonato, online): moedas para o melhor carro de gente desta máquina.
        void RaceCoins()
        {
            if (panel.CareerMode || !panel.RaceMode || testing) return;
            var ord = Order();
            int best = -1;
            for (int i = 0; i < ord.Count; i++)
            {
                int k = ord[i];
                bool mine = OnlineRun ? IsLocalLane(k) : sim.aiSkill[k] == 0;
                if (mine) { best = i; break; }
            }
            if (best < 0) return;
            int got = Progresso.RacePay(Champ.TrackHash(builder.ToSave()), ord.Count - 1 - best, OnlineRun);
            panel.Toast($"+{got} moedas (◎ {Progresso.P.moedas})");
        }

        // Prêmio de um nó da carreira: cada estrela nova paga uma vez (50, 75, 100); rival vencido e campeão, uma vez;
        // e a corrida em si, como fora da carreira (sem carros batidos).
        int CareerCoins(NodeDef n, int before, int after)
        {
            int c = 0;
            for (int s = before; s < after && s < Progresso.StarPrize.Length; s++) c += Progresso.StarPrize[s];
            if (n.tipo == "rival" && before == 0 && after > 0) c += Progresso.RivalPrize;
            if (n.tipo == "campeonato" && before < 2 && after >= 2) c += Progresso.ChampPrize[Mathf.Clamp(kRegion, 0, Progresso.ChampPrize.Length - 1)];
            Progresso.Earn(c);
            return c + Progresso.RacePay("carreira:" + n.id, 0, false);
        }
    }
}
