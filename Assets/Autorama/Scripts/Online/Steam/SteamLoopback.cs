using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace Autorama
{
    // Um par de conexões da Steam dentro do próprio processo (CreateSocketPair com loopback): serve para testar o caminho de
    // mensagens da corrida (`in`, `snap`, `ev`) sem uma segunda conta. Na corrida de verdade a conexão é com outro jogador
    // (SteamNetworkingMessages / Sockets pelo Datagram Relay) e a API de envio e leitura é esta.
    public class SteamLoopback : IDisposable
    {
        public HSteamNetConnection A, B;
        public bool Ok { get; private set; }

        public SteamLoopback()
        {
            var ia = new SteamNetworkingIdentity(); var ib = new SteamNetworkingIdentity();
            Ok = SteamNetworkingSockets.CreateSocketPair(out A, out B, true, ref ia, ref ib);
        }

        public static EResult Send(HSteamNetConnection conn, byte[] data, bool reliable)
        {
            var h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { return SteamNetworkingSockets.SendMessageToConnection(conn, h.AddrOfPinnedObject(), (uint)data.Length, reliable ? Constants.k_nSteamNetworkingSend_Reliable : Constants.k_nSteamNetworkingSend_Unreliable, out _); }
            finally { h.Free(); }
        }

        public static List<byte[]> Receive(HSteamNetConnection conn, int max = 64)
        {
            var list = new List<byte[]>();
            var ptrs = new IntPtr[max];
            int n = SteamNetworkingSockets.ReceiveMessagesOnConnection(conn, ptrs, max);
            for (int i = 0; i < n; i++)
            {
                var m = Marshal.PtrToStructure<SteamNetworkingMessage_t>(ptrs[i]);
                var bytes = new byte[m.m_cbSize];
                Marshal.Copy(m.m_pData, bytes, 0, m.m_cbSize);
                list.Add(bytes);
                SteamNetworkingMessage_t.Release(ptrs[i]);
            }
            return list;
        }

        public void Dispose()
        {
            if (!Ok) return;
            SteamNetworkingSockets.CloseConnection(A, 0, "fim do teste", false);
            SteamNetworkingSockets.CloseConnection(B, 0, "fim do teste", false);
            Ok = false;
        }
    }
}
