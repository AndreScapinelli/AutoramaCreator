#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Steamworks;
using UnityEditor;
using UnityEngine;

namespace Autorama
{
    // Testes da camada Steam com o app de teste da Valve (Spacewar, AppId 480). Precisam do cliente da Steam aberto e logado
    // e do steam_appid.txt (480) na raiz do projeto. Seção 24 do guia.
    //   Sozinho (uma conta):  script eval 'Autorama.SteamSpacewarTest.Run(); return Autorama.SteamSpacewarTest.Last;'
    //   Com duas contas:      uma máquina HostManual("Sala de teste", true), a outra JoinManual("CODIGO"); depois ManualSay/ManualLog.
    public static class SteamSpacewarTest
    {
        public static string Last = "";
        static readonly StringBuilder report = new StringBuilder();

        static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("FALHOU: " + what);
            report.AppendLine("ok  " + what);
            Debug.Log("[SteamSpacewarTest] ok  " + what);
        }

        static bool WaitFor(Func<bool> done, float seconds = 8f)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                SteamBoot.Tick();
                if (done()) return true;
                Thread.Sleep(20);
            }
            return false;
        }

        public static void Run()
        {
            report.Clear();
            SteamLobbyClient pub = null, priv = null;
            SteamLoopback loop = null;
            try
            {
                Check(SteamBoot.Init(), "a Steam inicia" + (SteamBoot.Ready ? "" : ": " + SteamBoot.Error));
                Check(SteamBoot.AppId == SteamBoot.SpacewarAppId, $"o app é o Spacewar (480): {SteamBoot.AppId}");
                Check(!string.IsNullOrEmpty(SteamBoot.PersonaName) && SteamBoot.MyId.IsValid(), $"conta logada: {SteamBoot.PersonaName} ({SteamBoot.MyId.m_SteamID})");

                // ---- sala pública ----
                pub = new SteamLobbyClient();
                bool created = false, createdOk = false;
                pub.Created += ok => { created = true; createdOk = ok; };
                pub.Create("Sala de teste Autorama", true);
                Check(WaitFor(() => created) && createdOk, "criar lobby público");
                Check(pub.InLobby && pub.IsOwner && pub.Owner == SteamBoot.MyId, "o criador é o dono (host) do lobby");
                string code = pub.Data("code");
                Check(pub.Data(SteamLobbyClient.Tag) == "1" && pub.Data("pub") == "1" && pub.Data("state") == "lobby" && pub.Data("name") == "Sala de teste Autorama" && code.Length == 5, $"dados do lobby gravados (código {code})");
                pub.SetData("stages", "2");
                Check(pub.Data("stages") == "2", "o dono muda os dados do lobby (etapas)");

                pub.SetMyData("color", "#2563EB"); pub.SetMyData("ready", "1");
                var me = pub.Members();
                Check(me.Count == 1 && me[0].id == SteamBoot.MyId && me[0].color == "#2563EB" && me[0].ready && me[0].name == SteamBoot.PersonaName, "cor e pronto do membro, com o nome da conta");

                // A lista pode demorar uns segundos para enxergar o lobby recém-criado.
                List<SteamLobbyInfo> found = null;
                for (int tentativa = 0; tentativa < 6 && (found == null || !found.Exists(l => l.code == code)); tentativa++)
                {
                    bool listed = false;
                    Action<List<SteamLobbyInfo>> h = l => { found = l; listed = true; };
                    pub.Listed += h; pub.Search(); WaitFor(() => listed); pub.Listed -= h;
                    if (!found.Exists(l => l.code == code)) WaitFor(() => false, 1f);
                }
                var mine = found?.Find(l => l.code == code);
                Check(mine != null, $"o lobby público aparece na lista de salas ({found?.Count ?? 0} sala(s) do Autorama)");
                Check(mine.name == "Sala de teste Autorama" && mine.players == 1 && mine.max == SteamLobbyClient.MaxMembers && mine.stages == 2 && mine.pub && mine.host == SteamBoot.PersonaName, "a lista traz nome, jogadores, máximo, etapas e host");

                string chatText = null; SteamMember chatWho = null;
                pub.Chat += (who, text) => { chatWho = who; chatText = text; };
                Check(pub.SendChat("  oi <b>sala</b> com acento: ação  "), "mandar chat");
                Check(WaitFor(() => chatText != null) && chatText == "oi <b>sala</b> com acento: ação" && chatWho.id == SteamBoot.MyId, "o chat volta com o texto inteiro (UTF-8) e o autor");

                // ---- sala privada: só pelo código ----
                priv = new SteamLobbyClient();
                created = false; priv.Created += ok => { created = true; createdOk = ok; };
                priv.Create("Sala privada", false);
                Check(WaitFor(() => created) && createdOk, "criar lobby privado");
                string pcode = priv.Data("code");
                Check(priv.Data("pub") == "0" && pcode.Length == 5 && pcode != code, $"a sala privada tem outro código ({pcode})");
                List<SteamLobbyInfo> byCode = null;
                for (int tentativa = 0; tentativa < 6 && (byCode == null || byCode.Count == 0); tentativa++)
                {
                    bool listed = false;
                    Action<List<SteamLobbyInfo>> h = l => { byCode = l; listed = true; };
                    pub.Listed += h; pub.Search(pcode); WaitFor(() => listed); pub.Listed -= h;
                    if (byCode.Count == 0) WaitFor(() => false, 1f);
                }
                Check(byCode.Count == 1 && byCode[0].code == pcode && !byCode[0].pub, "a sala privada é achada pelo código");
                bool listed2 = false; List<SteamLobbyInfo> open = null;
                pub.Listed += l => { open = l; listed2 = true; };
                pub.Search(); WaitFor(() => listed2);
                Check(open != null && !open.Exists(l => l.code == pcode), "a sala privada não aparece na lista pública");

                priv.Leave();
                Check(!priv.InLobby, "sair do lobby");

                // ---- mensagens da corrida (in, snap, ev) por um par de conexões da Steam ----
                loop = new SteamLoopback();
                Check(loop.Ok, "par de conexões em loopback criado");
                string inJson = JsonUtility.ToJson(new NetOut { t = "in", held = true, re = 3 });
                Check(SteamLoopback.Send(loop.A, Encoding.UTF8.GetBytes(inJson), true) == EResult.k_EResultOK, "enviar o botão (confiável)");
                var sim = new RaceSim(new RaceSettings());
                string snapJson = JsonUtility.ToJson(new NetOutSnap { snap = NetSync.Pack(sim, 2) });
                Check(SteamLoopback.Send(loop.A, Encoding.UTF8.GetBytes(snapJson), false) == EResult.k_EResultOK, $"enviar o snapshot ({snapJson.Length} bytes, não confiável)");
                var got = new List<byte[]>();
                WaitFor(() => { got.AddRange(SteamLoopback.Receive(loop.B)); return got.Count >= 2; });
                Check(got.Count == 2, $"as duas mensagens chegam ({got.Count})");
                var m0 = JsonUtility.FromJson<NetMsg>(Encoding.UTF8.GetString(got[0])); var m1 = JsonUtility.FromJson<NetMsg>(Encoding.UTF8.GetString(got[1]));
                Check(m0.t == "in" && m0.held && m0.re == 3, "o botão chega igual");
                Check(m1.t == "snap" && m1.snap.hi == 2 && m1.snap.cars.Count == 2 && Mathf.Abs(m1.snap.cars[0].s - RaceSim.GridS) < 1e-3f, "o snapshot chega inteiro e igual");
                var back = new List<byte[]>();
                SteamLoopback.Send(loop.B, Encoding.UTF8.GetBytes("{\"t\":\"ev\",\"k\":\"res\"}"), true);
                WaitFor(() => { back.AddRange(SteamLoopback.Receive(loop.A)); return back.Count >= 1; });
                Check(back.Count == 1 && Encoding.UTF8.GetString(back[0]).Contains("\"ev\""), "a mensagem volta no outro sentido");

                // O snapshot da prova tem ~1,2 KB: mede o que a Steam entrega sem confiabilidade.
                foreach (int size in new[] { 1200, 1500, 4000 })
                {
                    var payload = new byte[size]; for (int i = 0; i < size; i++) payload[i] = (byte)('a' + i % 26);
                    SteamLoopback.Send(loop.A, payload, false);
                    var rx = new List<byte[]>();
                    WaitFor(() => { rx.AddRange(SteamLoopback.Receive(loop.B)); return rx.Count >= 1; }, 2f);
                    report.AppendLine($"    mensagem não confiável de {size} bytes: {(rx.Count == 1 && rx[0].Length == size ? "chegou inteira" : "não chegou")}");
                }
                Debug.Log("[SteamSpacewarTest] TUDO CERTO");
            }
            catch (Exception e) { Debug.LogError("[SteamSpacewarTest] " + e.Message); report.AppendLine(e.Message); }
            finally
            {
                loop?.Dispose(); priv?.Dispose(); pub?.Dispose();
                SteamBoot.Tick();
                SteamBoot.Shutdown();
                Last = report.ToString();
            }
        }

        // ---------- Duas contas, à mão ----------

        static SteamLobbyClient manual;
        static readonly StringBuilder manualLog = new StringBuilder();
        public static string ManualLog => manualLog.ToString();

        static void Pump() => SteamBoot.Tick();

        static void Log(string s) { manualLog.AppendLine(s); Debug.Log("[Steam manual] " + s); }

        static void StartManual()
        {
            ManualStop();
            manualLog.Clear();
            if (!SteamBoot.Init()) { Log("Steam: " + SteamBoot.Error); return; }
            manual = new SteamLobbyClient();
            manual.Chat += (who, text) => Log($"{who.name}: {text}");
            manual.MembersChanged += () => Log("jogadores: " + string.Join(", ", manual.Members().ConvertAll(m => m.name + (m.ready ? " ✓" : "") + (m.id == manual.Owner ? " (host)" : ""))));
            manual.DataChanged += () => Log($"sala: {manual.Data("name")} · etapas {manual.Data("stages")} · {manual.Data("state")}");
            EditorApplication.update += Pump;
        }

        // Máquina A: cria a sala e mostra o código no log.
        public static void HostManual(string name, bool pub)
        {
            StartManual(); if (manual == null) return;
            manual.Created += ok => { if (ok) { manual.SetMyData("color", "#E0362C"); Log($"sala criada: código {manual.Data("code")} ({(pub ? "pública" : "privada")})"); } else Log("não deu para criar a sala"); };
            manual.Create(name, pub);
        }

        // Máquina B: procura pelo código (ou entra na primeira sala pública se vazio) e entra.
        public static void JoinManual(string code)
        {
            StartManual(); if (manual == null) return;
            manual.Listed += list =>
            {
                Log($"{list.Count} sala(s) achada(s)");
                if (list.Count > 0) { Log($"entrando em {list[0].name} ({list[0].code}) de {list[0].host}"); manual.Join(list[0].id); }
            };
            manual.Joined += ok => { Log(ok ? "entrei na sala" : "não consegui entrar"); if (ok) manual.SetMyData("color", "#2563EB"); };
            manual.Search(code);
        }

        public static void ManualSay(string text) => manual?.SendChat(text);
        public static void ManualReady(bool ready) => manual?.SetMyData("ready", ready ? "1" : "0");

        public static void ManualStop()
        {
            EditorApplication.update -= Pump;
            if (manual != null) { manual.Dispose(); manual = null; SteamBoot.Tick(); SteamBoot.Shutdown(); }
        }
    }
}
#endif
