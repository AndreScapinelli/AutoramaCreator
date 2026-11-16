using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Moedas e Loja (seção 74 do guia): a tela da Loja, as travas no construtor e as moedas de cada corrida.
    public partial class RaceController
    {
        VisualElement shopLayer, shopList;
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

            panel.ShopRequested += () => ShowShop(true);
            Progresso.Changed += OnProfileChanged;
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
