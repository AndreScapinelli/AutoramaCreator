using System;
using System.Collections.Generic;
using UnityEngine;

namespace Autorama
{
    // Moedas fora da carreira (seção 96 do guia): corrida livre, campeonato offline e online pagam um pouco por
    // corrida terminada. A carreira continua sendo onde mais se ganha (e tem o prêmio dela, em RaceController.Shop).
    public partial class RaceController
    {
        // Online (partida rápida e cada bateria do campeonato online): base + por pessoa que ficou atrás.
        public const int OnBase = 40, OnPerHuman = 20, OnDay = 3000;
        // Offline (corrida livre e campeonato offline): só com IA na pista; base + por IA atrás, pela qualidade dela.
        public const int OffBase = 5, OffPerAi = 10, OffDay = 2000;
        // A corrida curta paga menos: cheio a partir de 2 min, nunca menos de ¼.
        public const float FullTime = 120f, MinShare = 0.25f;

        string coinSig;
        int lastEarn;
        string earnNote;                               // a linha do resultado: o que ganhou e o limite do dia

        // Uma vez por corrida terminada (sig: a do resultado).
        void PayRace(string sig)
        {
            if (sig == coinSig || sim.phase != RaceSim.Phase.Done) return;
            coinSig = sig;
            lastEarn = 0;
            earnNote = null;
            var ord = new List<int>(Order());
            ord.RemoveAll(k => !sim.present[k]);
            bool online = OnlineRun;
            int me = -1;                               // online: o meu carro; offline: a pessoa mais bem colocada
            foreach (int k in ord)
                if (online ? IsMyLane(k) : sim.aiSkill[k] == 0) { me = k; break; }
            if (me < 0) return;
            float raw = 0f;
            bool anyAi = false;
            for (int i = ord.IndexOf(me) + 1; i < ord.Count; i++)
            {
                int k = ord[i];
                if (online) { if (!IsLocalLane(k) && sim.aiSkill[k] == 0) raw += OnPerHuman; }
                else if (sim.aiSkill[k] > 0) raw += OffPerAi * Mathf.Min(sim.aiSkill[k], 100) / 100f;
            }
            foreach (int k in ord) if (sim.aiSkill[k] > 0) anyAi = true;
            if (!online && !anyAi) { earnNote = L.T("Sem pilotos IA na pista: esta corrida não dá moedas."); return; }   // não dá para farmar sozinho
            raw += online ? OnBase : OffBase;
            float t = sim.cars[me].finish ?? sim.t;
            lastEarn = Earn(online, Mathf.RoundToInt(raw * Mathf.Clamp(t / FullTime, MinShare, 1f)));
            if (lastEarn > 0) panel.Toast(L.F("+{0} moedas", lastEarn));
            var d = Carteira.D;
            earnNote = online
                ? (lastEarn > 0 ? L.F("+{0} moedas", lastEarn) + " · " : "") + (d.hojeOn < OnDay ? L.F("hoje no online: {0} de {1} (depois, metade)", d.hojeOn, OnDay) : L.T("limite do dia no online passou: pagando metade"))
                : lastEarn > 0 ? L.F("+{0} moedas", lastEarn) + " · " + L.F("hoje fora do online: {0} de {1}", d.hojeOff, OffDay)
                : L.F("Limite do dia fora do online ({0}) atingido. Amanhã tem mais; a carreira e o online continuam pagando.", OffDay);
        }

        bool IsMyLane(int k) => HeatNow(out _, out _, out _, out _, out var pl) && k >= 0 && k < pl.Length && pl[k] != null && pl[k].id == myId;

        // O limite do dia: online paga cheio até OnDay e metade depois; offline para em OffDay.
        static int Earn(bool online, int n)
        {
            var d = Carteira.D;
            string hoje = DateTime.Now.ToString("yyyy-MM-dd");
            if (d.dia != hoje) { d.dia = hoje; d.hojeOn = d.hojeOff = 0; }
            if (online)
            {
                int full = Mathf.Clamp(OnDay - d.hojeOn, 0, n);
                n = full + (n - full) / 2;
                d.hojeOn += n;
            }
            else
            {
                n = Mathf.Clamp(OffDay - d.hojeOff, 0, n);
                d.hojeOff += n;
            }
            if (n <= 0) return 0;
            d.moedas += n;
            Carteira.Save();
            return n;
        }
    }
}
