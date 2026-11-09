using System;

namespace Autorama
{
    // O que a aba Online precisa da rede, seja o servidor Node (OnlineClient) ou a Steam (SteamTransport).
    // As mensagens são as mesmas (NetOut… para enviar, NetMsg para receber); só muda quem as entrega (seção 24 do guia).
    public interface IOnlineTransport
    {
        OnlineClient.State state { get; }
        event Action Opened;
        event Action<string> Closed;            // o motivo, em português (não dispara em Close())
        event Action<NetMsg> Message;
        void Connect(string address);
        void Send(object msg);
        void Close();
        void Pump();                            // na thread principal, a cada quadro
    }
}
