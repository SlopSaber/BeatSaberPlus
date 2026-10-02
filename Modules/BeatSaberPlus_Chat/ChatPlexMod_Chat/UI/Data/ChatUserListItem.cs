using CP_SDK.Chat.Interfaces;

namespace ChatPlexMod_Chat.UI.Data
{
    /// <summary>
    /// Chat user list item
    /// </summary>
    internal class ChatUserListItem : CP_SDK.UI.Data.IListItem
    {
        public IChatService Service;
        public IChatUser    User;
        public string       Text;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="p_Service">Chat service</param>
        /// <param name="p_User">Chat user</param>
        public ChatUserListItem(IChatService p_Service, IChatUser p_User)
        {
            Service = p_Service;
            User    = p_User;

            var l_ServiceName  = Service.DisplayName;
            var l_IsModerator  = User.IsModerator || User.IsBroadcaster;
            var l_IsVip        = !l_IsModerator && User.IsVip;
            var l_IsSubscriber = !l_IsModerator && !l_IsVip && User.IsSubscriber;
            Text = FormatText(l_ServiceName, User.DisplayName, l_IsModerator, l_IsVip, l_IsSubscriber);
        }

        internal ChatUserListItem(IChatService p_Service, IChatUser p_User, string p_Text)
        {
            Service = p_Service;
            User    = p_User;
            Text    = p_Text;
        }

        internal static string FormatText(string p_ServiceName, string p_DisplayName, bool p_IsModerator, bool p_IsVip, bool p_IsSubscriber)
        {
            var l_Role = p_IsModerator ? "🗡 <color=yellow>"
                       : p_IsVip ? "💎 <color=red>"
                       : p_IsSubscriber ? "👑 <color=#008eff>"
                       : string.Empty;
            return "<align=\"left\">[" + p_ServiceName + "] " + l_Role + p_DisplayName;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On show
        /// </summary>
        public override void OnShow()
        {
            if (!(Cell is CP_SDK.UI.Data.TextListCell l_TextListCell))
                return;

            l_TextListCell.Text.SetText(Text);
        }
        /// <summary>
        /// On hide
        /// </summary>
        public override void OnHide() { }
    }
}
