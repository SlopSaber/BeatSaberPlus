using CP_SDK_WebSocketSharp;
using CP_SDK_WebSocketSharp.Server;

namespace BeatSaberPlus_SongOverlay.Network
{
    /// <summary>
    /// Web socket client session
    /// </summary>
    internal class OverlaySession : WebSocketBehavior
    {
        private OverlayTransport m_Transport;

        internal void Bind(OverlayTransport p_Transport)
        {
            m_Transport = p_Transport;
        }

        /// <summary>
        /// On connection open
        /// </summary>
        protected override void OnOpen()
        {
            m_Transport?.OnClientConnected(this);
        }
        /// <summary>
        /// On connection close
        /// </summary>
        /// <param name="p_Event"></param>
        protected override void OnClose(CloseEventArgs p_Event)
        {
            m_Transport?.OnClientDisconnected(this);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Send data to the client
        /// </summary>
        /// <param name="p_Data">Data to send</param>
        internal void SendData(string p_Data)
        {
            Send(p_Data);
        }
    }
}
