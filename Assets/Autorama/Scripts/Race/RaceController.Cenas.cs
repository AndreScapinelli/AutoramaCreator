using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using static Autorama.BuilderPanel;

namespace Autorama
{
    // Cenas do enredo na carreira (seção 106): quadrinho por cima da tela, retrato com as iniciais na cor do personagem,
    // nome e fala. Enter, Espaço ou clique avançam; Esc pula a cena. Cada cena aparece uma vez por save (CareerSave.vistas).
    public partial class RaceController
    {
        VisualElement kCena, kCenaPic;
        Label kCenaIni, kCenaNome, kCenaTexto;
        readonly List<Fala> kFalas = new List<Fala>();
        readonly List<CenaDef> kCenasNew = new List<CenaDef>();     // as "depois" que esta prova abriu (Finish → ShowResult)
        int kFalaI;
        Action kCenaDone;

        bool CenaOn => kCena != null && kCena.style.display == DisplayStyle.Flex;

        void BuildCenaUI()
        {
            kCena = El("k-full k-dim k-cena", kLayer);
            kCena.style.display = DisplayStyle.None;
            kCena.RegisterCallback<PointerDownEvent>(e => { CenaNext(); e.StopPropagation(); });
            var box = El("k-panel k-cena-box", kCena);
            kCenaPic = El("k-cena-pic", box);
            kCenaIni = Text("", "k-cena-ini", kCenaPic);
            var col = El("k-cena-col", box);
            kCenaNome = Text("", "k-cena-nome", col);
            kCenaTexto = Text("", "k-cena-texto", col);
            Text(L.T("Enter ou clique continua · Esc pula"), "k-cena-dica", col);
        }

        // Toca as cenas ainda não vistas da lista, uma depois da outra; done roda no fim (ou já, se não há nenhuma).
        bool PlayCenas(IEnumerable<CenaDef> list, Action done)
        {
            if (kSave == null || kCena == null) return false;
            if (CenaOn) return false;
            kSave.vistas ??= new List<string>();
            var novas = new List<Fala>();
            foreach (var c in list)
            {
                if (kSave.vistas.Contains(c.id)) continue;
                kSave.vistas.Add(c.id);
                novas.AddRange(c.falas);
            }
            if (novas.Count == 0) return false;
            kFalas.Clear();
            kFalas.AddRange(novas);
            kFalaI = 0;
            kCenaDone = done;
            kCena.style.display = DisplayStyle.Flex;
            kCena.BringToFront();
            ShowFala();
            Career.Save(kSlot, kSave);       // por último: salvar redesenha o mapa, que volta a perguntar pelas cenas
            return true;
        }

        void ShowFala()
        {
            var f = kFalas[kFalaI];
            bool narra = string.IsNullOrEmpty(f.quem);
            kCenaPic.style.display = narra ? DisplayStyle.None : DisplayStyle.Flex;
            kCenaNome.style.display = narra ? DisplayStyle.None : DisplayStyle.Flex;
            kCenaTexto.EnableInClassList("k-cena-narra", narra);
            if (!narra)
            {
                var c = Cenas.Cor(f.quem);
                kCenaPic.style.backgroundColor = c;
                kCenaIni.style.color = c.grayscale > 0.6f ? new Color(0.1f, 0.12f, 0.11f) : Color.white;
                kCenaIni.text = Cenas.Iniciais(f.quem);
                kCenaNome.text = L.T(f.quem);
                kCenaNome.style.color = c;
            }
            kCenaTexto.text = f.texto;
        }

        void CenaNext()
        {
            if (!CenaOn) return;
            if (++kFalaI < kFalas.Count) { ShowFala(); return; }
            CenaClose();
        }

        void CenaClose()
        {
            kCena.style.display = DisplayStyle.None;
            var d = kCenaDone;
            kCenaDone = null;
            d?.Invoke();
        }

        // Mapa de uma região ainda sem estrelas (um save que já passou dela não vê a chegada).
        void CenaRegiao()
        {
            if (CenaOn || kSave == null || kScreen != KScreen.Map) return;
            if (Career.RegionStars(kSave, KRegion) > 0) return;
            PlayCenas(Cenas.De("regiao", KRegion.id), null);
        }

        // Antes de correr um nó ainda sem estrelas; no fim, corre.
        bool CenaAntes()
        {
            if (CenaOn || KNode == null || Career.Stars(kSave, KNode.id) > 0) return false;
            return PlayCenas(Cenas.De("antes", KNode.id), CareerRun);
        }

        // Depois: o nó passou das estrelas que a cena pede nesta prova. Derrota: a prova não deu estrela e o nó continua sem nenhuma.
        void CenasDepois(int before, int after)
        {
            kCenasNew.Clear();
            foreach (var c in Cenas.De("depois", KNode.id)) if (before < Cenas.Estrelas(c) && after >= Cenas.Estrelas(c)) kCenasNew.Add(c);
            if (after == 0) kCenasNew.AddRange(Cenas.De("derrota", KNode.id));
        }
    }
}
