using CP_SDK.XUI;
using System.Collections.Generic;
using CP_SDK.Chat.Interfaces;
using CP_SDK.Chat.Models.Twitch;
using CP_SDK.Chat.Services.Twitch;
using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEngine.UI;

namespace ChatPlexMod_Chat.UI
{
    /// <summary>
    /// Moderation right view
    /// </summary>
    internal sealed class ModerationRightView : CP_SDK.UI.ViewController<ModerationRightView>
    {
        private XUIVVList m_List = null;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private List<Data.ChatUserListItem>  m_Items         = new List<Data.ChatUserListItem>();
        private Data.ChatUserListItem        m_SelectedItem  = null;
        private long                        m_RefreshRevision;
        private bool                        m_ViewActive;
        private RefreshRequest              m_PendingRefresh;
        private Task<List<PreparedUserRow>>  m_Preparation;

        private sealed class UserRowSnapshot
        {
            internal int Index;
            internal string ServiceName;
            internal string DisplayName;
            internal bool IsModerator;
            internal bool IsBroadcaster;
            internal bool IsVip;
            internal bool IsSubscriber;
        }

        private sealed class PreparedUserRow
        {
            internal int Index;
            internal string DisplayName;
            internal string Text;
        }

        private sealed class RefreshRequest
        {
            internal long Revision;
            internal List<(IChatService, IChatUser)> Bindings;
            internal UserRowSnapshot[] Rows;
            internal CompareInfo Comparison;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public Data.ChatUserListItem SelectedItem => m_SelectedItem;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On view creation
        /// </summary>
        protected override void OnViewCreation()
        {
            Templates.FullRectLayout(
                Templates.TitleBar("Channel Active Users (Last 40 ones)"),

                XUIHLayout.Make(
                    XUIVVList.Make()
                        .SetListCellPrefab(CP_SDK.UI.Data.ListCellPrefabs<CP_SDK.UI.Data.TextListCell>.Get())
                        .OnListItemSelected(OnListItemSelect)
                        .Bind(ref m_List)
                )
                .SetHeight(55)
                .SetSpacing(0)
                .SetPadding(0)
                .SetBackground(true)
                .OnReady(x => x.CSizeFitter.horizontalFit = x.CSizeFitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained)
                .OnReady(x => x.HOrVLayoutGroup.childForceExpandWidth = true)
                .OnReady(x => x.HOrVLayoutGroup.childForceExpandHeight = true),

                Templates.ExpandedButtonsLine(
                    XUIPrimaryButton.Make("TimeOut(10 min)").OnClick(OnTimeOutButton),
                    XUIPrimaryButton.Make("Ban").OnClick(OnBanButton),
                    XUIPrimaryButton.Make("Mod").OnClick(OnModButton),
                    XUIPrimaryButton.Make("UnMod").OnClick(OnUnModButton)
                )
            )
            .SetBackground(true, null, true)
            .BuildUI(transform);
        }
        /// <summary>
        /// On view activation
        /// </summary>
        protected override void OnViewActivation()
        {
            m_ViewActive = true;
            Refresh();
        }

        protected override void OnViewDeactivation()
        {
            m_ViewActive = false;
            ++m_RefreshRevision;
            m_PendingRefresh = null;
        }

        protected override void OnViewDestruction()
            => OnViewDeactivation();

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Refresh event list
        /// </summary>
        internal void Refresh()
        {
            var l_Revision = ++m_RefreshRevision;
            m_PendingRefresh = null;
            var l_Users = Chat.Instance.LastChatUsers;

            if (!m_ViewActive || l_Users.Count == 0)
            {
                PublishImmediate(l_Users);
                return;
            }

            foreach (var l_User in l_Users)
            {
                if (l_User.Item1?.GetType() != typeof(TwitchService) || l_User.Item2?.GetType() != typeof(TwitchUser))
                {
                    PublishImmediate(l_Users);
                    return;
                }
            }

            var l_Rows = new UserRowSnapshot[l_Users.Count];
            for (int l_I = 0; l_I < l_Users.Count; ++l_I)
            {
                var l_User = l_Users[l_I].Item2;
                var l_DisplayName = l_User.DisplayName;
                if (l_DisplayName == null)
                {
                    PublishImmediate(l_Users);
                    return;
                }

                l_Rows[l_I] = new UserRowSnapshot
                {
                    Index = l_I,
                    ServiceName = l_Users[l_I].Item1.DisplayName,
                    DisplayName = l_DisplayName,
                    IsModerator = l_User.IsModerator,
                    IsBroadcaster = l_User.IsBroadcaster,
                    IsVip = l_User.IsVip,
                    IsSubscriber = l_User.IsSubscriber
                };
            }

            var l_Request = new RefreshRequest
            {
                Revision = l_Revision,
                Bindings = l_Users,
                Rows = l_Rows,
                Comparison = CultureInfo.CurrentCulture.CompareInfo
            };

            if (m_Preparation != null)
                m_PendingRefresh = l_Request;
            else
                StartPreparation(l_Request);
        }

        private void PublishImmediate(List<(IChatService, IChatUser)> p_Users)
        {
            m_Items.Clear();
            foreach (var l_User in p_Users)
                m_Items.Add(new Data.ChatUserListItem(l_User.Item1, l_User.Item2));
            m_Items.Sort((x, y) => x.User.DisplayName.CompareTo(y.User.DisplayName));

            m_List.SetListItems(m_Items);
        }

        private static List<PreparedUserRow> PrepareRows(UserRowSnapshot[] p_Rows, CompareInfo p_Comparison)
        {
            var l_Result = new List<PreparedUserRow>(p_Rows.Length);
            foreach (var l_Row in p_Rows)
            {
                l_Result.Add(new PreparedUserRow
                {
                    Index = l_Row.Index,
                    DisplayName = l_Row.DisplayName,
                    Text = Data.ChatUserListItem.FormatText(l_Row.ServiceName, l_Row.DisplayName,
                        l_Row.IsModerator || l_Row.IsBroadcaster, l_Row.IsVip, l_Row.IsSubscriber)
                });
            }

            l_Result.Sort((x, y) => p_Comparison.Compare(x.DisplayName, y.DisplayName, CompareOptions.None));
            return l_Result;
        }

        private static Task<List<PreparedUserRow>> StartWorker(UserRowSnapshot[] p_Rows, CompareInfo p_Comparison)
            => Task.Run(() => PrepareRows(p_Rows, p_Comparison));

        private async void StartPreparation(RefreshRequest p_Request)
        {
            var l_Task = StartWorker(p_Request.Rows, p_Request.Comparison);
            m_Preparation = l_Task;

            List<PreparedUserRow> l_Result = null;
            Exception l_Error = null;
            try
            {
                l_Result = await l_Task.ConfigureAwait(false);
            }
            catch (Exception p_Exception)
            {
                l_Error = p_Exception;
            }

            CP_SDK.Unity.MTMainThreadInvoker.Enqueue(() => CompletePreparation(p_Request, l_Result, l_Error));
        }

        private void CompletePreparation(RefreshRequest p_Request, List<PreparedUserRow> p_Rows, Exception p_Error)
        {
            try
            {
                if (!this || !m_ViewActive || !UICreated || !gameObject.activeInHierarchy || !CurrentScreen || p_Request.Revision != m_RefreshRevision)
                    return;

                if (p_Error != null)
                {
                    Logger.Instance.Error(p_Error);
                    PublishImmediate(p_Request.Bindings);
                    return;
                }

                m_Items.Clear();
                foreach (var l_Row in p_Rows)
                {
                    var l_Binding = p_Request.Bindings[l_Row.Index];
                    m_Items.Add(new Data.ChatUserListItem(l_Binding.Item1, l_Binding.Item2, l_Row.Text));
                }
                m_List.SetListItems(m_Items);
            }
            finally
            {
                m_Preparation = null;
                var l_Pending = m_PendingRefresh;
                m_PendingRefresh = null;
                if (this && m_ViewActive && UICreated && gameObject.activeInHierarchy && CurrentScreen
                    && l_Pending != null && l_Pending.Revision == m_RefreshRevision)
                    StartPreparation(l_Pending);
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On user selected
        /// </summary>
        /// <param name="p_SelectedItem">Selected item</param>
        private void OnListItemSelect(CP_SDK.UI.Data.IListItem p_SelectedItem)
            => m_SelectedItem = (Data.ChatUserListItem)p_SelectedItem;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// TimeOut an user
        /// </summary>
        private void OnTimeOutButton()
        {
            if (!EnsureItemSelected() || !EnsurePermissions())
                return;

            ShowConfirmationModal($"Do you really want to <b>TimeOut</b> user\n{m_SelectedItem.User.DisplayName}?", (x) => {
                if (!x)
                    return;

                foreach (var l_Current in CP_SDK.Chat.Service.Multiplexer.Channels)
                {
                    if (l_Current.Item1 is CP_SDK.Chat.Services.Twitch.TwitchService)
                        l_Current.Item1.SendTextMessage(l_Current.Item2, $"/timeout {m_SelectedItem.User.UserName}");
                }
            });
        }
        /// <summary>
        /// Ban an user
        /// </summary>
        private void OnBanButton()
        {
            if (!EnsureItemSelected() || !EnsurePermissions())
                return;

            ShowConfirmationModal($"Do you really want to <b>Ban</b> user\n{m_SelectedItem.User.DisplayName}?", (x) => {
                if (!x)
                    return;

                foreach (var l_Current in CP_SDK.Chat.Service.Multiplexer.Channels)
                {
                    if (l_Current.Item1 is CP_SDK.Chat.Services.Twitch.TwitchService)
                        l_Current.Item1.SendTextMessage(l_Current.Item2, $"/ban {m_SelectedItem.User.UserName}");
                }
            });
        }
        /// <summary>
        /// Mod an user
        /// </summary>
        private void OnModButton()
        {
            if (!EnsureItemSelected() || !EnsurePermissions())
                return;

            ShowConfirmationModal($"Do you really want to <b>Mod</b> user\n{m_SelectedItem.User.DisplayName}?", (x) => {
                if (!x)
                    return;

                foreach (var l_Current in CP_SDK.Chat.Service.Multiplexer.Channels)
                {
                    if (l_Current.Item1 is CP_SDK.Chat.Services.Twitch.TwitchService)
                        m_SelectedItem.Service.SendTextMessage(l_Current.Item2, $"/mod {m_SelectedItem.User.UserName}");
                }
            });
        }
        /// <summary>
        /// UnMod an user
        /// </summary>
        private void OnUnModButton()
        {
            if (!EnsureItemSelected() || !EnsurePermissions())
                return;

            ShowConfirmationModal($"Do you really want to <b>UnMod</b> user\n{m_SelectedItem.User.DisplayName}?", (x) => {
                if (!x)
                    return;

                foreach (var l_Current in CP_SDK.Chat.Service.Multiplexer.Channels)
                {
                    if (l_Current.Item1 is CP_SDK.Chat.Services.Twitch.TwitchService)
                        l_Current.Item1.SendTextMessage(l_Current.Item2, $"/unmod {m_SelectedItem.User.UserName}");
                }
            });
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Ensure that an shortcut is selected
        /// </summary>
        /// <returns></returns>
        private bool EnsureItemSelected()
        {
            if (m_SelectedItem == null)
            {
                ShowMessageModal("Please select an user first!");
                return false;
            }

            return true;
        }
        /// <summary>
        /// Ensure permissions
        /// </summary>
        /// <returns></returns>
        private bool EnsurePermissions()
        {
            if (!(m_SelectedItem.Service is CP_SDK.Chat.Services.Twitch.TwitchService))
            {
                ShowMessageModal("Only twitch is supported at the moment!");
                return false;
            }

            return true;
        }
    }
}
