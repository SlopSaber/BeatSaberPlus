using System.Collections;
using System.Linq;
using UnityEngine;

namespace ChatPlexMod_SongChartVisualizer
{
    /// <summary>
    /// SongChartVisualizer Module
    /// </summary>
    public class SongChartVisualizer : CP_SDK.ModuleBase<SongChartVisualizer>
    {
        public const int MaxPoints = 100;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        public override CP_SDK.EIModuleBaseType             Type                => CP_SDK.EIModuleBaseType.Integrated;
        public override string                              Name                => "Song Chart Visualizer";
        public override string                              Description         => "Get spoiled about the map difficulty!";
        public override string                              DocumentationURL    => "https://github.com/hardcpp/BeatSaberPlus/wiki#song-chart-visualizer";
        public override bool                                UseChatFeatures     => false;
        public override bool                                IsEnabled           { get => SCVConfig.Instance.Enabled; set { SCVConfig.Instance.Enabled = value; SCVConfig.Instance.Save(); } }
        public override CP_SDK.EIModuleBaseActivationType   ActivationType      => CP_SDK.EIModuleBaseActivationType.OnMenuSceneLoaded;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private UI.SettingsLeftView     m_SettingsLeftView  = null;
        private UI.SettingsMainView     m_SettingsMainView  = null;
        private UI.SettingsRightView    m_SettingsRightView = null;

        private Transform                           m_RootTransform             = null;
        private CP_SDK.UI.Components.CFloatingPanel m_ChartFloatingPanel        = null;
        private UI.ChartFloatingPanelView           m_ChartFloatingPanelView    = null;
        private object                              m_GraphSession              = new object();
        private Data.GraphBuilder.GraphOperation    m_GraphOperation;
        private bool                                m_ModuleActive;

#if BEATSABER
        private AudioTimeSyncController m_AudioTimeSyncController = null;
#else
#error Missing game implementation
#endif

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Enable the Module
        /// </summary>
        protected override void OnEnable()
        {
            RetireGraphPreparation();
            m_ModuleActive = true;
            /// Bind event
            CP_SDK.ChatPlexSDK.OnGenericSceneChange += ChatPlexSDK_OnGenericSceneChange;
#if BEATSABER
            CP_SDK_BS.Game.Logic.OnLevelStarted += Game_LevelStarted;
#else
#error Missing game implementation
#endif

            try
            {
                /// Master GameObject
                m_RootTransform = new GameObject("ChatPlexMod_SongChartVisualizer").transform;
                m_RootTransform.transform.position = Vector3.zero;
                m_RootTransform.transform.rotation = Quaternion.identity;

                GameObject.DontDestroyOnLoad(m_RootTransform);

                m_ChartFloatingPanel = CP_SDK.UI.UISystem.FloatingPanelFactory.Create("ChartFloatingPanel", m_RootTransform);
                m_ChartFloatingPanel.SetSize(new Vector2(105.0f, 65.0f));
                m_ChartFloatingPanel.SetBackground(true);
                m_ChartFloatingPanel.SetBackgroundColor(SCVConfig.Instance.BackgroundColor);
                m_ChartFloatingPanel.OnSceneRelease(CP_SDK.EGenericScene.Playing, ChartFloatingPanel_OnRelease);
                m_ChartFloatingPanel.SetRadius(0f);

                m_ChartFloatingPanelView = CP_SDK.UI.UISystem.CreateViewController<UI.ChartFloatingPanelView>();
                m_ChartFloatingPanel.SetViewController(m_ChartFloatingPanelView);

                m_RootTransform.gameObject.SetActive(false);
            }
            catch (System.Exception l_Exception)
            {
                Logger.Instance.Error("[ChatPlexMod_SongChartVisualizer][SongChartVisualizer.OnEnable] Failed to destroy floating panel");
                Logger.Instance.Error(l_Exception);
            }
        }
        /// <summary>
        /// Disable the Module
        /// </summary>
        protected override void OnDisable()
        {
            m_ModuleActive = false;
            RetireGraphPreparation();
            try
            {
                CP_SDK.UI.UISystem.DestroyUI(ref m_ChartFloatingPanel, ref m_ChartFloatingPanelView);

                if (m_RootTransform)
                    GameObject.Destroy(m_RootTransform.gameObject);

                /// Reset variables
                m_RootTransform             = null;
            }
            catch (System.Exception l_Exception)
            {
                Logger.Instance.Error("[ChatPlexMod_SongChartVisualizer][SongChartVisualizer.OnDisable] Failed to destroy floating panel");
                Logger.Instance.Error(l_Exception);
            }

            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsLeftView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsMainView);
            CP_SDK.UI.UISystem.DestroyUI(ref m_SettingsRightView);

            /// Unbind event
            CP_SDK.ChatPlexSDK.OnGenericSceneChange -= ChatPlexSDK_OnGenericSceneChange;
#if BEATSABER
            CP_SDK_BS.Game.Logic.OnLevelStarted -= Game_LevelStarted;
#else
#error Missing game implementation
#endif
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

            /// Change main view
            return (m_SettingsMainView, m_SettingsLeftView, m_SettingsRightView);
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// On generic scene change
        /// </summary>
        /// <param name="p_GenericScene"></param>
        private void ChatPlexSDK_OnGenericSceneChange(CP_SDK.EGenericScene p_GenericScene)
        {
            if (p_GenericScene != CP_SDK.EGenericScene.Menu)
                return;

            RetireGraphPreparation();
            if (m_RootTransform)
                m_RootTransform.gameObject.SetActive(false);
        }
#if BEATSABER
        /// <summary>
        /// When a level start
        /// </summary>
        /// <param name="p_Data">Level data</param>
        private void Game_LevelStarted(CP_SDK_BS.Game.LevelData p_Data)
        {
            RetireGraphPreparation();
            if (m_RootTransform)
                m_RootTransform.gameObject.SetActive(false);
            /// Not enabled in multi-player
            if (p_Data.Type == CP_SDK_BS.Game.LevelType.Multiplayer)
                return;

            /// Start the task
            var l_Session = m_GraphSession;
            var l_View = m_ChartFloatingPanelView;
            if (IsCurrent(l_Session, l_View))
                CP_SDK.Unity.MTCoroutineStarter.Start(PrepareFloatingPanel(l_Session, l_View, p_Data));
        }
#else
#error Missing game implementation
#endif

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Set preview enabled
        /// </summary>
        /// <param name="p_Enabled">Is enabled?</param>
        internal void SetPreview(bool p_Enabled)
        {
            RetireGraphPreparation();
            if (m_RootTransform)
                m_RootTransform.gameObject.SetActive(false);
            var l_Session = m_GraphSession;
            var l_View = m_ChartFloatingPanelView;
            if (!p_Enabled || !IsCurrent(l_Session, l_View))
                return;

            m_ChartFloatingPanel.SetTransformDirect(new Vector3(3.38f, 1.20f, 2.29f), new Vector3(0.00f, 58.00f, 0.00f));
            m_ChartFloatingPanel.SetLockIcon(CP_SDK.UI.Components.CFloatingPanel.ECorner.None);
            if (!IsCurrent(l_Session, l_View))
                return;
            var l_Operation = Data.GraphBuilder.PrepareSample(UI.ChartFloatingPanelView.GraphAreaWidth,
                UI.ChartFloatingPanelView.GraphAreaHeight, l_View.LegendCount, UI.ChartFloatingPanelView.CaptureLegendNumberFormat());
            m_GraphOperation = l_Operation;
            CP_SDK.Unity.MTCoroutineStarter.Start(PreparePreview(l_Session, l_View, l_Operation));
        }

        private IEnumerator PreparePreview(object p_Session, UI.ChartFloatingPanelView p_View, Data.GraphBuilder.GraphOperation p_Operation)
        {
            while (!p_Operation.Completion.IsCompleted)
            {
                if (!IsCurrentPreparation(p_Session, p_View, p_Operation))
                    yield break;
                yield return null;
            }
            if (ApplyPreparation(p_Session, p_View, p_Operation))
                m_RootTransform.gameObject.SetActive(true);
        }

        private void RetireGraphPreparation()
        {
            m_GraphSession = new object();
            m_GraphOperation?.Retire();
            m_GraphOperation = null;
#if BEATSABER
            m_AudioTimeSyncController = null;
#endif
            if (m_ChartFloatingPanelView)
            {
                m_ChartFloatingPanelView.SetGetSongTimeFunction(null);
                m_ChartFloatingPanelView.SetRotationFollow(null, null);
            }
        }

        private bool IsCurrent(object p_Session, UI.ChartFloatingPanelView p_View)
            => m_ModuleActive && ReferenceEquals(m_GraphSession, p_Session)
                && ReferenceEquals(m_ChartFloatingPanelView, p_View) && p_View && m_RootTransform && m_ChartFloatingPanel != null;

#if BEATSABER
        private bool IsCurrentLevel(object p_Session, UI.ChartFloatingPanelView p_View, CP_SDK_BS.Game.LevelData p_Level)
            => IsCurrent(p_Session, p_View) && ReferenceEquals(CP_SDK_BS.Game.Logic.LevelData, p_Level);

        private bool IsCurrentPreparation(object p_Session, UI.ChartFloatingPanelView p_View, Data.GraphBuilder.GraphOperation p_Operation,
            CP_SDK_BS.Game.LevelData p_Level = null)
            => IsCurrent(p_Session, p_View) && ReferenceEquals(m_GraphOperation, p_Operation) && !p_Operation.Retired
                && (p_Level == null || ReferenceEquals(CP_SDK_BS.Game.Logic.LevelData, p_Level));

        private bool ApplyPreparation(object p_Session, UI.ChartFloatingPanelView p_View, Data.GraphBuilder.GraphOperation p_Operation,
            CP_SDK_BS.Game.LevelData p_Level = null)
        {
            if (!IsCurrentPreparation(p_Session, p_View, p_Operation) || !p_Operation.Completion.IsCompleted)
                return false;
            if (p_Operation.Error != null)
            {
                Logger.Instance.Error("[ChatPlexMod_SongChartVisualizer][SongChartVisualizer] Failed to prepare graph");
                Logger.Instance.Error(p_Operation.Error);
                return false;
            }
            return p_View.SetPreparedGraph(p_Operation.Graph, p_Operation.Layout,
                () => IsCurrentPreparation(p_Session, p_View, p_Operation, p_Level));
        }
#else
#error Missing game implementation
#endif

        /// <summary>
        /// Update style
        /// </summary>
        internal void UpdateStyle()
        {
            m_ChartFloatingPanel?.SetBackgroundColor(SCVConfig.Instance.BackgroundColor);
            m_ChartFloatingPanelView?.UpdateStyle();
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Toggle chat visibility
        /// </summary>
        public void ToggleVisibility()
        {
            if (m_RootTransform && m_RootTransform.localScale.x > 0.5f)
                m_RootTransform.localScale = Vector3.zero;
            else if (m_RootTransform)
                m_RootTransform.localScale = Vector3.one;
        }
        /// <summary>
        /// Set visible
        /// </summary>
        /// <param name="p_Visible">Is visible</param>
        public void SetVisible(bool p_Visible)
        {
            if (!m_RootTransform)
                return;

            m_RootTransform.localScale = p_Visible ? Vector3.one : Vector3.zero;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Create the chart visualizer
        /// </summary>
        /// <returns></returns>
#if BEATSABER
        private IEnumerator PrepareFloatingPanel(object p_Session, UI.ChartFloatingPanelView p_View, CP_SDK_BS.Game.LevelData p_Level)
        {
            yield return new WaitForEndOfFrame();
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;

            if (p_Level?.Data?.playerSpecificSettings?.noTextsAndHuds ?? false)
                yield break;
            var l_TransformedBeatmapData = p_Level?.Data?.transformedBeatmapData;
            var l_BeatmapLevel = p_Level?.Data?.beatmapLevel;
            var l_SongDuration = l_BeatmapLevel?.songDuration ?? -1f;
            if (l_TransformedBeatmapData == null || l_BeatmapLevel == null || l_SongDuration == -1f)
                yield break;
            var l_HasRotation = p_Level?.HasRotations ?? false;
            var l_Position = l_HasRotation ? SCVConfig.Instance.ChartRotatingPosition : SCVConfig.Instance.ChartStandardPosition;
            var l_Rotation = l_HasRotation ? SCVConfig.Instance.ChartRotatingRotation : SCVConfig.Instance.ChartStandardRotation;
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;
            m_ChartFloatingPanel.SetTransformDirect(l_Position, l_Rotation);

            if ((int)(l_SongDuration + 1f) <= 0)
            {
                var l_Graph = Data.GraphBuilder.BuildNPSGraph(l_TransformedBeatmapData, l_SongDuration);
                if (!IsCurrentLevel(p_Session, p_View, p_Level))
                    yield break;
                var l_Layout = Data.GraphBuilder.PrepareLayout(l_Graph, UI.ChartFloatingPanelView.GraphAreaWidth,
                    UI.ChartFloatingPanelView.GraphAreaHeight, p_View.LegendCount, null);
                if (!p_View.SetPreparedGraph(l_Graph, l_Layout, () => IsCurrentLevel(p_Session, p_View, p_Level)))
                    yield break;
            }
            else
            {
                var l_Times = Data.GraphBuilder.CaptureNoteTimes(l_TransformedBeatmapData, l_SongDuration);
                if (!IsCurrentLevel(p_Session, p_View, p_Level))
                    yield break;
                var l_Operation = Data.GraphBuilder.Prepare(l_Times, l_SongDuration, UI.ChartFloatingPanelView.GraphAreaWidth,
                    UI.ChartFloatingPanelView.GraphAreaHeight, p_View.LegendCount, UI.ChartFloatingPanelView.CaptureLegendNumberFormat());
                m_GraphOperation = l_Operation;
                while (!l_Operation.Completion.IsCompleted)
                {
                    if (!IsCurrentPreparation(p_Session, p_View, l_Operation, p_Level))
                        yield break;
                    yield return null;
                }
                if (!ApplyPreparation(p_Session, p_View, l_Operation, p_Level))
                    yield break;
            }
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;
            m_ChartFloatingPanel.SetLockIcon(SCVConfig.Instance.ShowLockIcon
                ? CP_SDK.UI.Components.CFloatingPanel.ECorner.TopRight : CP_SDK.UI.Components.CFloatingPanel.ECorner.None);
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;
            m_RootTransform.gameObject.SetActive(true);

            var l_Waiter = new WaitForSeconds(0.25f);
            var l_AudioController = null as AudioTimeSyncController;
            while (l_AudioController == null)
            {
                if (!IsCurrentLevel(p_Session, p_View, p_Level) || CP_SDK.ChatPlexSDK.ActiveGenericScene != CP_SDK.EGenericScene.Playing)
                    yield break;
                l_AudioController = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().FirstOrDefault();
                if (l_AudioController != null)
                    break;
                yield return l_Waiter;
            }
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;
            m_AudioTimeSyncController = l_AudioController;
            p_View.SetGetSongTimeFunction(() => m_AudioTimeSyncController?.songTime ?? 0f);

            var l_RotationFollow = null as Transform;
            if (l_HasRotation)
            {
                var l_Attempt = 0;
                while (l_RotationFollow == null)
                {
                    if (!IsCurrentLevel(p_Session, p_View, p_Level) || CP_SDK.ChatPlexSDK.ActiveGenericScene != CP_SDK.EGenericScene.Playing)
                        yield break;
                    var l_FlyingGameHUDRotation = Resources.FindObjectsOfTypeAll<FlyingGameHUDRotation>().FirstOrDefault();
                    if (l_FlyingGameHUDRotation != null)
                    {
                        l_RotationFollow = l_FlyingGameHUDRotation.transform;
                        break;
                    }
                    l_Attempt++;
                    if (l_Attempt > 3)
                        break;
                    yield return l_Waiter;
                }
            }
            if (!IsCurrentLevel(p_Session, p_View, p_Level))
                yield break;
            p_View.SetRotationFollow(m_RootTransform, l_RotationFollow);
            if (!l_RotationFollow && m_RootTransform)
                m_RootTransform.localRotation = Quaternion.identity;
        }
#else
#error Missing game implementation
#endif
        /// <summary>
        /// When the floating panel is moved
        /// </summary>
        /// <param name="p_LocalPosition">New local position</param>
        /// <param name="p_LocalEulerAngles">New local euler angles</param>
        private void ChartFloatingPanel_OnRelease(Vector3 p_LocalPosition, Vector3 p_LocalEulerAngles)
        {
#if BEATSABER
            var l_HasRotation = CP_SDK_BS.Game.Logic.LevelData?.HasRotations ?? false;
#else
#error Missing game implementation
#endif

            if (!l_HasRotation)
            {
                SCVConfig.Instance.ChartStandardPosition = p_LocalPosition;
                SCVConfig.Instance.ChartStandardRotation = p_LocalEulerAngles;
            }
            else
            {
                SCVConfig.Instance.ChartRotatingPosition = p_LocalPosition;
                SCVConfig.Instance.ChartRotatingRotation = p_LocalEulerAngles;
            }
        }
    }
}
