using CP_SDK_WebSocketSharp.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BeatSaberPlus_SongOverlay.Network
{
    internal sealed class OverlayTransport
    {
        internal sealed class Startup
        {
            private readonly string m_GameVersion;
            private readonly string m_PlayerName;
            private readonly string m_PlayerID;
            private readonly bool m_Indented;
            private readonly string m_OwnerText;

            internal Startup(string p_GameVersion, string p_PlayerName, string p_PlayerID, bool p_Indented)
            {
                m_GameVersion = p_GameVersion;
                m_PlayerName = p_PlayerName;
                m_PlayerID = p_PlayerID;
                m_Indented = p_Indented;
                if (JsonConvert.DefaultSettings != null)
                    m_OwnerText = JsonConvert.SerializeObject(CreateObject(), p_Indented ? Formatting.Indented : Formatting.None);
            }

            private JObject CreateObject()
            {
                return new JObject()
                {
                    ["_type"] = "handshake",
                    ["protocolVersion"] = 1,
                    ["gameVersion"] = m_GameVersion,
                    ["playerName"] = m_PlayerName,
                    ["playerPlatformId"] = m_PlayerID,
                };
            }

            internal string Encode()
            {
                return m_OwnerText ?? SerializeOwned(CreateObject(), m_Indented);
            }
        }

        internal sealed class Payload
        {
            private readonly Models.Event m_Data;
            private readonly string m_OwnerText;
            private string m_WorkerText;

            internal Payload(Models.Event p_OwnedData)
            {
                m_Data = p_OwnedData;
                if (JsonConvert.DefaultSettings != null)
                    m_OwnerText = JsonConvert.SerializeObject(p_OwnedData);
            }

            internal string Encode()
            {
                return m_OwnerText ?? (m_WorkerText ?? (m_WorkerText = SerializeOwned(m_Data, false)));
            }
        }

        private sealed class Connection
        {
            internal readonly OverlaySession Session;
            internal Payload[] Initial;
            internal bool Initialized;
            internal volatile bool Closed;

            internal Connection(OverlaySession p_Session)
            {
                Session = p_Session;
            }
        }

        private readonly object m_Gate = new object();
        private readonly List<Connection> m_Clients = new List<Connection>();
        private readonly Payload[] m_Pending = new Payload[5];
        private readonly Startup m_Startup;
        private int m_Stopped;

        internal OverlayTransport(Startup p_Startup)
        {
            m_Startup = p_Startup;
        }

        internal bool HasClients
        {
            get
            {
                lock (m_Gate)
                    return Volatile.Read(ref m_Stopped) == 0 && m_Clients.Count != 0;
            }
        }

        internal bool NeedsInitialSnapshot
        {
            get
            {
                lock (m_Gate)
                {
                    if (Volatile.Read(ref m_Stopped) != 0)
                        return false;
                    foreach (var l_Client in m_Clients)
                        if (!l_Client.Initialized && l_Client.Initial == null)
                            return true;
                    return false;
                }
            }
        }

        internal void Stop()
        {
            Volatile.Write(ref m_Stopped, 1);
        }

        internal void OnClientConnected(OverlaySession p_Client)
        {
            lock (m_Gate)
            {
                if (Volatile.Read(ref m_Stopped) != 0)
                    return;
                foreach (var l_Client in m_Clients)
                    if (ReferenceEquals(l_Client.Session, p_Client))
                        return;
                m_Clients.Add(new Connection(p_Client));
            }
        }

        internal void OnClientDisconnected(OverlaySession p_Client)
        {
            lock (m_Gate)
            {
                for (int l_I = 0; l_I < m_Clients.Count; ++l_I)
                {
                    if (!ReferenceEquals(m_Clients[l_I].Session, p_Client))
                        continue;
                    m_Clients[l_I].Closed = true;
                    m_Clients.RemoveAt(l_I);
                    return;
                }
            }
        }

        internal void InitializePending(Payload[] p_OwnedInitial)
        {
            lock (m_Gate)
            {
                if (Volatile.Read(ref m_Stopped) != 0)
                    return;
                foreach (var l_Client in m_Clients)
                    if (!l_Client.Initialized && l_Client.Initial == null)
                        l_Client.Initial = p_OwnedInitial;
            }
        }

        internal bool PublishUpdates(Payload[] p_OwnedUpdates)
        {
            lock (m_Gate)
            {
                if (Volatile.Read(ref m_Stopped) != 0 || m_Clients.Count == 0)
                    return false;
                for (int l_I = 0; l_I < m_Pending.Length; ++l_I)
                    if (p_OwnedUpdates[l_I] != null)
                        m_Pending[l_I] = p_OwnedUpdates[l_I];
                return true;
            }
        }

        internal void Run()
        {
            HttpServer l_Server = null;
            try
            {
                if (Volatile.Read(ref m_Stopped) != 0)
                    return;

                var l_Handshake = m_Startup.Encode();
                l_Server = new HttpServer(2947);
                l_Server.AddWebSocketService<OverlaySession>("/socket", InitializeSession);
                if (Volatile.Read(ref m_Stopped) != 0)
                    return;
                l_Server.Start();

                while (Volatile.Read(ref m_Stopped) == 0)
                {
                    if (HasClients)
                        Drain(l_Handshake);
                    Thread.Sleep(33);
                }
            }
            catch (Exception l_Exception)
            {
                ReportError(l_Exception);
            }
            finally
            {
                Stop();
                try { l_Server?.Stop(); }
                catch (Exception l_Exception) { ReportError(l_Exception); }
                lock (m_Gate)
                {
                    m_Clients.Clear();
                    Array.Clear(m_Pending, 0, m_Pending.Length);
                }
            }
        }

        private void InitializeSession(OverlaySession p_Client)
        {
            p_Client.Bind(this);
        }

        private void Drain(string p_Handshake)
        {
            Connection[] l_Clients;
            Payload[] l_Updates = null;
            List<KeyValuePair<Connection, Payload[]>> l_Initials = null;
            lock (m_Gate)
            {
                var l_HasWork = false;
                var l_HasReadyClients = false;
                foreach (var l_Client in m_Clients)
                {
                    l_HasWork |= !l_Client.Initialized && l_Client.Initial != null;
                    l_HasReadyClients |= l_Client.Initialized;
                }
                var l_HasUpdates = false;
                foreach (var l_Payload in m_Pending)
                    l_HasUpdates |= l_Payload != null;
                if (!l_HasWork && !(l_HasReadyClients && l_HasUpdates))
                    return;

                l_Clients = m_Clients.ToArray();
                var l_HasInitializedClients = false;
                foreach (var l_Client in l_Clients)
                {
                    if (!l_Client.Initialized && l_Client.Initial != null)
                    {
                        if (l_Initials == null)
                            l_Initials = new List<KeyValuePair<Connection, Payload[]>>();
                        l_Initials.Add(new KeyValuePair<Connection, Payload[]>(l_Client, l_Client.Initial));
                        l_Client.Initial = null;
                        l_Client.Initialized = true;
                    }
                    l_HasInitializedClients |= l_Client.Initialized;
                }
                if (l_HasInitializedClients && l_HasUpdates)
                {
                    l_Updates = (Payload[])m_Pending.Clone();
                    Array.Clear(m_Pending, 0, m_Pending.Length);
                }
            }

            // Socket I/O never holds the gate used by owner snapshots or socket callbacks.
            if (l_Initials != null)
            {
                foreach (var l_Initial in l_Initials)
                {
                    if (!Send(l_Initial.Key, p_Handshake))
                        continue;
                    foreach (var l_Payload in l_Initial.Value)
                    {
                        if (Volatile.Read(ref m_Stopped) != 0 || l_Initial.Key.Closed)
                            break;
                        if (!Send(l_Initial.Key, l_Payload.Encode()))
                            break;
                    }
                }
            }

            if (l_Updates == null)
                return;
            for (int l_I = 0; l_I < l_Updates.Length; ++l_I)
            {
                if (Volatile.Read(ref m_Stopped) != 0)
                    return;
                if (l_Updates[l_I] == null)
                    continue;
                var l_Text = l_Updates[l_I].Encode();
                foreach (var l_Client in l_Clients)
                    if (l_Client.Initialized)
                        Send(l_Client, l_Text);
            }
        }

        private bool Send(Connection p_Client, string p_Text)
        {
            if (Volatile.Read(ref m_Stopped) != 0 || p_Client.Closed)
                return false;
            try
            {
                p_Client.Session.SendData(p_Text);
                return true;
            }
            catch (Exception l_Exception)
            {
                OnClientDisconnected(p_Client.Session);
                ReportError(l_Exception);
                return false;
            }
        }

        private static string SerializeOwned(object p_Value, bool p_Indented)
        {
            using (var l_Text = new StringWriter(CultureInfo.InvariantCulture))
            using (var l_Writer = new JsonTextWriter(l_Text) { Formatting = p_Indented ? Formatting.Indented : Formatting.None })
            {
                JsonSerializer.Create().Serialize(l_Writer, p_Value);
                return l_Text.ToString();
            }
        }

        private static void ReportError(Exception p_Exception)
        {
            CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() => Logger.Instance.Error(p_Exception));
        }

        internal static void ObserveFault(Task p_Task)
        {
            ReportError(p_Task.Exception);
        }
    }
}
