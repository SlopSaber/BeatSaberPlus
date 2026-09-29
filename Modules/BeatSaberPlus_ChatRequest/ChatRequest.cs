using IPA.Utilities;
using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatSaberPlus_ChatRequest
{
    /// <summary>
    /// Chat Request instance
    /// </summary>
    public partial class ChatRequest : CP_SDK.ModuleBase<ChatRequest>
    {
        public override CP_SDK.EIModuleBaseType             Type                => CP_SDK.EIModuleBaseType.Integrated;
        public override string                              Name                => "Chat Request";
        public override string                              Description         => "Take song request from your chat!";
        public override string                              DocumentationURL    => "https://github.com/hardcpp/BeatSaberPlus/wiki#chat-request";
        public override bool                                UseChatFeatures     => true;
        public override bool                                IsEnabled           { get => CRConfig.Instance.Enabled; set { CRConfig.Instance.Enabled = value; CRConfig.Instance.Save(); } }
        public override CP_SDK.EIModuleBaseActivationType   ActivationType      => CP_SDK.EIModuleBaseActivationType.OnMenuSceneLoaded;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private UI.SettingsLeftView     m_SettingsLeftView  = null;
        private UI.SettingsMainView     m_SettingsMainView  = null;
        private UI.SettingsRightView    m_SettingsRightView = null;

        /// <summary>
        /// Create button coroutine
        /// </summary>
        private Coroutine m_CreateButtonCoroutine = null;
        /// <summary>
        /// Manager button
        /// </summary>
        private Button m_ManagerButtonP = null;
        /// <summary>
        /// Manager button
        /// </summary>
        private Button m_ManagerButtonS = null;
        private RectTransform m_TitleBarOverlay = null;
        /// <summary>
        /// Manager flow coordinator
        /// </summary>
        /// <summary>
        /// Chat core instance
        /// </summary>
        private bool m_ChatCoreAcquired = false;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Enable the Module
        /// </summary>
        protected override void OnEnable()
        {
            /// Create directory
            try
            {
                var l_Path = System.IO.Path.GetDirectoryName(m_DBFilePath);
                if (!System.IO.Directory.Exists(l_Path))
                    System.IO.Directory.CreateDirectory(l_Path);
            }
            catch (System.Exception l_Exception)
            {
                Logger.Instance.Error($"[ChatRequest][ChatRequest.OnEnable] Failed to create directory \"{System.IO.Path.GetDirectoryName(m_DBFilePath)}\"");
                Logger.Instance.Error(l_Exception);
            }

            /// Try to load DB
            LoadDatabase();
            OnQueueChanged(true, true);

            /// Build command table
            BuildCommandTable();

            /// Bind events
            CP_SDK_BS.Game.Logic.OnMenuSceneLoaded += OnMenuSceneLoaded;
            CP_SDK_BS.Game.Logic.OnSceneChange     += OnSceneChange;

            if (!m_ChatCoreAcquired)
            {
                /// Init chat core
                m_ChatCoreAcquired = true;
                CP_SDK.Chat.Service.Acquire();

                /// Run all services
                CP_SDK.Chat.Service.Multiplexer.OnTextMessageReceived += ChatCoreMutiplixer_OnTextMessageReceived;
            }

            /// Add button
            if (m_CreateButtonCoroutine == null)
                m_CreateButtonCoroutine = CP_SDK.Unity.MTCoroutineStarter.Start(CreateButtonCoroutine());

            /// Set queue status
            QueueOpen = CRConfig.Instance.QueueOpen;
        }
        /// <summary>
        /// Disable the Module
        /// </summary>
        protected override void OnDisable()
        {
            /// Save database
            SaveDatabase();

            /// Unbind events
            CP_SDK_BS.Game.Logic.OnMenuSceneLoaded -= OnMenuSceneLoaded;
            CP_SDK_BS.Game.Logic.OnSceneChange     -= OnSceneChange;

            /// Un-init chat core
            if (m_ChatCoreAcquired)
            {
                /// Unbind services
                CP_SDK.Chat.Service.Multiplexer.OnTextMessageReceived -= ChatCoreMutiplixer_OnTextMessageReceived;

                /// Stop all chat services
                CP_SDK.Chat.Service.Release();
                m_ChatCoreAcquired = false;
            }

            /// Stop coroutine
            if (m_CreateButtonCoroutine != null)
            {
                CP_SDK.Unity.MTCoroutineStarter.Stop(m_CreateButtonCoroutine);
                m_CreateButtonCoroutine = null;
            }

            /// Destroy manager button
            if (m_TitleBarOverlay != null)
            {
                GameObject.Destroy(m_TitleBarOverlay.gameObject);
                m_TitleBarOverlay = null;
            }
            m_ManagerButtonP = null;
            m_ManagerButtonS = null;

            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsLeftView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsMainView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsRightView);

            UI.ManagerViewFlowCoordinator.Destroy();

            /// Clear database
            SongQueue.Clear();
            SongHistory.Clear();
            SongAllowlist.Clear();
            SongBlocklist.Clear();
            BannedUsers.Clear();
            BannedMappers.Clear();
            Remaps.Clear();
            m_RequestedThisSessionID = new System.Collections.Concurrent.ConcurrentBag<string>();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Get Module settings UI
        /// </summary>
        protected override (CP_SDK.UI.IViewController, CP_SDK.UI.IViewController, CP_SDK.UI.IViewController) GetSettingsViewControllersImplementation()
        {
            if (m_SettingsLeftView == null)     m_SettingsLeftView  = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsLeftView>();
            if (m_SettingsMainView == null)     m_SettingsMainView  = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsMainView>();
            if (m_SettingsRightView == null)    m_SettingsRightView = CP_SDK.UI.UISystem.CreateViewController<UI.SettingsRightView>();

            return (m_SettingsMainView, m_SettingsLeftView, m_SettingsRightView);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// When the menu loaded
        /// </summary>
        private void OnMenuSceneLoaded()
        {
            if (m_ManagerButtonP == null || !m_ManagerButtonP || m_ManagerButtonS == null || !m_ManagerButtonS)
            {
                UI.ManagerViewFlowCoordinator.Destroy();

                /// Stop coroutine
                if (m_CreateButtonCoroutine != null)
                {
                    CP_SDK.Unity.MTCoroutineStarter.Stop(m_CreateButtonCoroutine);
                    m_CreateButtonCoroutine = null;
                }

                /// Destroy manager button
                if (m_TitleBarOverlay != null)
                {
                    GameObject.Destroy(m_TitleBarOverlay.gameObject);
                    m_TitleBarOverlay = null;
                }
                m_ManagerButtonP = null;
                m_ManagerButtonS = null;

                /// Add button
                if (m_CreateButtonCoroutine == null)
                    m_CreateButtonCoroutine = CP_SDK.Unity.MTCoroutineStarter.Start(CreateButtonCoroutine());
            }
        }
        /// <summary>
        /// When the active scene is changed
        /// </summary>
        /// <param name="p_Scene">New scene</param>
        private void OnSceneChange(CP_SDK_BS.Game.Logic.ESceneType p_Scene)
        {
            if (p_Scene == CP_SDK_BS.Game.Logic.ESceneType.Menu)
                UpdateButton();
            else if (p_Scene == CP_SDK_BS.Game.Logic.ESceneType.Playing)
            {
                try
                {
                    var l_CurrentMap    = CP_SDK_BS.Game.Logic.LevelData?.Data?.beatmapLevel;
                    var l_Mapper        = l_CurrentMap?.allMappers?.FirstOrDefault();

                    if (m_LastPlayingLevel != l_CurrentMap)
                    {
                        m_LastPlayingLevel = l_CurrentMap;

                        if (CRConfig.Instance.SafeMode2)
                            m_LastPlayingLevelResponse = $" ";
                        else
                            m_LastPlayingLevelResponse = $"{l_CurrentMap.songName} by {l_Mapper}";

                        if (CP_SDK_BS.Game.Levels.LevelID_IsCustom(l_CurrentMap.levelID) && CP_SDK_BS.Game.Levels.TryGetHashFromLevelID(l_CurrentMap.levelID, out var l_Hash))
                        {
                            var l_CachedEntry = null as Models.SongEntry;

                            lock (SongHistory)
                                l_CachedEntry = SongHistory.FirstOrDefault(x => x.GetLevelHash() == l_Hash);

                            if (l_CachedEntry == null)
                            {
                                CP_SDK_BS.Game.BeatMapsClient.GetOnlineByHash(l_Hash, (p_Valid, p_BeatMap) =>
                                {
                                    if (   !p_Valid
                                        || p_BeatMap == null
                                        || l_CurrentMap != (CP_SDK_BS.Game.Logic.LevelData?.Data?.beatmapLevel ?? null)
                                        )
                                        return;

                                    m_LastPlayingLevelResponseLink = "https://beatsaver.com/maps/" + p_BeatMap.id;
                                });
                            }
                            else if (l_CachedEntry.BeatSaver_Map != null)
                            {
                                m_LastPlayingLevelResponseLink = "https://beatsaver.com/maps/" + l_CachedEntry.BeatSaver_Map.id;
                            }
                        }
                    }
                }
                catch (System.Exception p_Exception)
                {
                    Logger.Instance.Error("ChatRequest OnSceneChange");
                    Logger.Instance.Error(p_Exception);
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Create button coroutine
        /// </summary>
        /// <returns></returns>
        private IEnumerator CreateButtonCoroutine()
        {
            var l_LevelSelectionNavigationController    = null as LevelSelectionNavigationController;
            var l_Waiter                                = new WaitForSeconds(0.25f);
            while (true)
            {
                if (!l_LevelSelectionNavigationController)
                    l_LevelSelectionNavigationController = Resources.FindObjectsOfTypeAll<LevelSelectionNavigationController>().LastOrDefault();

                if (l_LevelSelectionNavigationController != null && l_LevelSelectionNavigationController.gameObject.transform.childCount >= 2)
                    break;

                yield return l_Waiter;
            }

            m_TitleBarOverlay = CP_SDK_BS.UI.Button.CreateMenuTitleBarOverlay(l_LevelSelectionNavigationController.transform);
            m_ManagerButtonP = CP_SDK_BS.UI.Button.CreatePrimary(m_TitleBarOverlay, "Chat Request", () => UI.ManagerViewFlowCoordinator.Instance().Present(), null, 29f);
            m_ManagerButtonP.transform.localPosition = new Vector3(53f, 0f, 0f);
            m_ManagerButtonP.transform.localScale    = new Vector3(0.8f, 1.4f, 0.8f);
            m_ManagerButtonP.gameObject.SetActive(false);
            var primaryText = m_ManagerButtonP.GetComponentInChildren<TextMeshProUGUI>();
            primaryText.margin = Vector4.zero;
            primaryText.rectTransform.localScale = new Vector3(1.75f, 1f, 1f);
            primaryText.textWrappingMode = TextWrappingModes.NoWrap;
            primaryText.enableAutoSizing = true;
            primaryText.fontSizeMin = 2f;
            primaryText.fontSizeMax = 2.5f;

            m_ManagerButtonS = CP_SDK_BS.UI.Button.Create(m_TitleBarOverlay, "Chat Request", () => UI.ManagerViewFlowCoordinator.Instance().Present(), null, 29f);
            m_ManagerButtonS.transform.localPosition = new Vector3(53f, 0f, 0f);
            m_ManagerButtonS.transform.localScale    = new Vector3(0.8f, 1.4f, 0.8f);
            var edgeSprite = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sprite => sprite.name == "RoundRect8");
            if (edgeSprite != null)
            {
                var primaryBackground = m_ManagerButtonP.transform.Find("BG")?.GetComponent<Image>();
                var secondaryBackground = m_ManagerButtonS.transform.Find("BG")?.GetComponent<Image>();
                if (primaryBackground != null) primaryBackground.sprite = edgeSprite;
                if (secondaryBackground != null) secondaryBackground.sprite = edgeSprite;
            }
            m_ManagerButtonS.gameObject.SetActive(true);
            var secondaryText = m_ManagerButtonS.GetComponentInChildren<TextMeshProUGUI>();
            secondaryText.margin = Vector4.zero;
            secondaryText.rectTransform.localScale = new Vector3(1.75f, 1f, 1f);
            secondaryText.textWrappingMode = TextWrappingModes.NoWrap;
            secondaryText.enableAutoSizing = true;
            secondaryText.fontSizeMin = 2f;
            secondaryText.fontSizeMax = 2.5f;

            // BSML removes a prefab LayoutElement with deferred Destroy. Measure after
            // that removal and the regular button's layout, then keep the blue state's
            // ContentSizeFitter from replacing the matched height on activation.
            yield return null;
            var secondaryRect = (RectTransform)m_ManagerButtonS.transform;
            var primaryRect = (RectTransform)m_ManagerButtonP.transform;
            LayoutRebuilder.ForceRebuildLayoutImmediate(secondaryRect);
            var primaryLayout = m_ManagerButtonP.GetComponent<LayoutElement>();
            primaryLayout.minHeight = secondaryRect.rect.height;
            primaryLayout.preferredHeight = secondaryRect.rect.height;
            MatchButtonRect(secondaryRect, primaryRect);
            MatchButtonRect(m_ManagerButtonS.transform.Find("BG") as RectTransform, m_ManagerButtonP.transform.Find("BG") as RectTransform);
            MatchButtonRect(m_ManagerButtonS.transform.Find("Content") as RectTransform, m_ManagerButtonP.transform.Find("Content") as RectTransform);

            UpdateButton();

            m_CreateButtonCoroutine = null;
        }

        private static void MatchButtonRect(RectTransform source, RectTransform target)
        {
            if (source == null || target == null)
                return;

            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.sizeDelta = source.sizeDelta;
            target.anchoredPosition = source.anchoredPosition;
        }

        /// <summary>
        /// Update button text
        /// </summary>
        internal void UpdateButton()
        {
            if (m_ManagerButtonP == null || m_ManagerButtonS == null)
                return;

            m_ManagerButtonP.transform.localPosition = new Vector3(53f, 0f, 0f);
            m_ManagerButtonP.transform.localScale = new Vector3(0.8f, 1.4f, 0.8f);

            m_ManagerButtonS.transform.localPosition = new Vector3(53f, 0f, 0f);
            m_ManagerButtonS.transform.localScale = new Vector3(0.8f, 1.4f, 0.8f);

            m_ManagerButtonP.gameObject.SetActive(SongQueue.Count != 0);
            m_ManagerButtonS.gameObject.SetActive(SongQueue.Count == 0);
        }
    }
}
