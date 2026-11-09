using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Autorama
{
    // Conexão WebSocket com o servidor de salas. A rede roda em tarefas; tudo o que chega vira evento na fila e
    // só é entregue em Pump(), chamado a cada quadro pela thread principal (o JsonUtility e a UI não são thread-safe).
    // Os await usam ConfigureAwait(false): a rede não depende da thread principal.
    // Não funciona em WebGL (System.Net.WebSockets não existe lá): o jogo é de computador.
    public class OnlineClient : IOnlineTransport
    {
        public enum State { Closed, Connecting, Open }

        public const int ConnectTimeoutMs = 8000;
        // Versão das mensagens (servidor/servidor.js, PROTOCOL): sobe junto nos dois quando o formato muda.
        public const int Protocol = 1;
        // O servidor de verdade (wss://…). Vazio até o deploy: aí o padrão é o servidor local, e o campo da aba continua editável.
        public const string ProductionServer = "";
        public static string DefaultServer => Application.isEditor || ProductionServer == "" ? "ws://localhost:8080" : ProductionServer;
        const int MaxMessage = 4 * 1024 * 1024;

        public State state { get; private set; }
        public event Action Opened;
        public event Action<string> Closed;            // o motivo, em português
        public event Action<NetMsg> Message;

        class Conn
        {
            public ClientWebSocket sock = new ClientWebSocket();
            public CancellationTokenSource cts = new CancellationTokenSource();
            public readonly ConcurrentQueue<string> outbox = new ConcurrentQueue<string>();
            public readonly SemaphoreSlim signal = new SemaphoreSlim(0);
            public int gen;
        }

        struct Ev { public int gen, kind; public string text; }       // kind: 0 abriu, 1 mensagem, 2 fechou
        readonly ConcurrentQueue<Ev> events = new ConcurrentQueue<Ev>();
        Conn conn;
        int gen;

        // Aceita "host:porta" sem o esquema; sem esquema vira ws://.
        public static bool TryParseUrl(string text, out Uri uri)
        {
            text = (text ?? "").Trim();
            if (text.Length > 0 && !text.Contains("://")) text = "ws://" + text;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && (uri.Scheme == "ws" || uri.Scheme == "wss");
        }

        public void Connect(string url)
        {
            Close();
            int g = ++gen;
            if (!TryParseUrl(url, out var uri)) { events.Enqueue(new Ev { gen = g, kind = 2, text = "Endereço do servidor inválido." }); return; }
            state = State.Connecting;
            var c = conn = new Conn { gen = g };
            c.sock.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
            _ = Run(c, uri);
        }

        public void Send(object msg)
        {
            var c = conn;
            if (c == null || state == State.Closed) return;
            c.outbox.Enqueue(JsonUtility.ToJson(msg));
            c.signal.Release();
        }

        // Fecha sem aviso de "Closed": quem chamou já sabe.
        public void Close()
        {
            var c = conn;
            conn = null; state = State.Closed; gen++;
            if (c == null) return;
            c.cts.Cancel();
            try { c.sock.Abort(); } catch { }
        }

        public void Pump()
        {
            while (events.TryDequeue(out var e))
            {
                if (e.gen != gen) continue;               // sobrou de uma conexão antiga
                if (e.kind == 0) { state = State.Open; Send(new NetOut { t = "hello", ver = Protocol }); Opened?.Invoke(); }
                else if (e.kind == 1)
                {
                    NetMsg m = null;
                    try { m = JsonUtility.FromJson<NetMsg>(e.text); } catch { }
                    if (m != null && !string.IsNullOrEmpty(m.t)) Message?.Invoke(m);
                }
                else { conn = null; state = State.Closed; Closed?.Invoke(e.text); }
            }
        }

        async Task Run(Conn c, Uri uri)
        {
            string reason = "A conexão com o servidor foi encerrada.";
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(c.cts.Token))
                {
                    timeout.CancelAfter(ConnectTimeoutMs);
                    try { await c.sock.ConnectAsync(uri, timeout.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!c.cts.IsCancellationRequested) { throw new TimeoutException(); }
                }
                events.Enqueue(new Ev { gen = c.gen, kind = 0 });
                _ = SendLoop(c);

                var buf = new byte[8192];
                var ms = new MemoryStream();
                while (c.sock.State == WebSocketState.Open && !c.cts.IsCancellationRequested)
                {
                    var r = await c.sock.ReceiveAsync(new ArraySegment<byte>(buf), c.cts.Token).ConfigureAwait(false);
                    if (r.MessageType == WebSocketMessageType.Close) { reason = "O servidor encerrou a conexão."; break; }
                    ms.Write(buf, 0, r.Count);
                    if (ms.Length > MaxMessage) { reason = "O servidor mandou uma mensagem grande demais."; break; }
                    if (!r.EndOfMessage) continue;
                    events.Enqueue(new Ev { gen = c.gen, kind = 1, text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length) });
                    ms.SetLength(0);
                }
            }
            catch (OperationCanceledException) { }          // fechado por Close()
            catch (TimeoutException) { reason = "Não foi possível conectar: tempo esgotado. Confira o endereço do servidor."; }
            catch (Exception e) { reason = "Não foi possível falar com o servidor: " + (e.InnerException ?? e).Message; }
            finally
            {
                if (!c.cts.IsCancellationRequested) events.Enqueue(new Ev { gen = c.gen, kind = 2, text = reason });
                c.cts.Cancel();
                try { c.sock.Dispose(); } catch { }
            }
        }

        // Um envio por vez: o ClientWebSocket não aceita dois SendAsync ao mesmo tempo.
        async Task SendLoop(Conn c)
        {
            try
            {
                while (!c.cts.IsCancellationRequested)
                {
                    await c.signal.WaitAsync(c.cts.Token).ConfigureAwait(false);
                    while (c.outbox.TryDequeue(out var text))
                        await c.sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(text)), WebSocketMessageType.Text, true, c.cts.Token).ConfigureAwait(false);
                }
            }
            catch { }       // o laço de leitura percebe a queda e avisa
        }
    }
}
