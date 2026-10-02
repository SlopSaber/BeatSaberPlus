using IPA.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace BeatSaberPlus_SongOverlay.Network
{
    /// <summary>
    /// Socket server
    /// </summary>
    class OverlayServer
    {
        private static OverlayTransport m_Transport;
        private static Task m_TransportTask;
        private static long m_ServerGeneration;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        private static Models.Event m_GameStateEvent    = new Models.Event() { gameStateChanged = "None" };
        private static Models.Event m_MapInfoEvent      = new Models.Event() { mapInfoChanged = new Models.MapInfo() };
        private static Models.Event m_PauseEvent        = new Models.Event() { pauseTime = 0 };
        private static Models.Event m_ResumeEvent       = new Models.Event() { resumeTime = 0 };
        private static Models.Event m_ScoreEvent        = new Models.Event() { scoreEvent = new Models.Score() };

        private static long m_MapInfoRevision;
        private static long m_MapInfoPublishedRevision;
        private static long m_GameStateRevision;
        private static long m_GameStatePublishedRevision;
        private static long m_PauseRevision;
        private static long m_PausePublishedRevision;
        private static long m_ResumeRevision;
        private static long m_ResumePublishedRevision;
        private static long m_ScoreRevision;
        private static long m_ScorePublishedRevision;

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Is the gameplay paused
        /// </summary>
        private static bool m_IsPaused;
        /// <summary>
        /// Audio time sync controller
        /// </summary>
        private static AudioTimeSyncController m_AudioTimeSyncController;
        /// <summary>
        /// Score controller instance
        /// </summary>
        private static ScoreController m_ScoreController;
        /// <summary>
        /// Combo controller instance
        /// </summary>
        private static ComboController m_ComboController;
        /// <summary>
        /// Game energy counter instance
        /// </summary>
        private static GameEnergyCounter m_GameEnergyCounter;
        /// <summary>
        /// Pause controller
        /// </summary>
        private static PauseController m_PauseController;

        private static bool m_CoverEnabled;
        private static long m_GameplayGeneration;
        private static bool m_CoverReady = true;
        private static GameplaySubscriptions m_GameplaySubscriptions;

        private sealed class GameplaySubscriptions
        {
            private readonly long m_Generation;
            private readonly object m_LevelData;
            private readonly ComboController m_Combo;
            private readonly ScoreController m_Score;
            private readonly GameEnergyCounter m_Energy;
            private readonly PauseController m_Pause;
            private readonly BeatmapObjectManager m_Beatmap;

            internal GameplaySubscriptions(long p_Generation, object p_LevelData)
            {
                m_Generation = p_Generation;
                m_LevelData = p_LevelData;
                m_Combo = m_ComboController;
                m_Score = m_ScoreController;
                m_Energy = m_GameEnergyCounter;
                m_Pause = m_PauseController;
                m_Beatmap = m_Score._beatmapObjectManager;
            }

            private bool IsCurrent => ReferenceEquals(m_GameplaySubscriptions, this) && IsCurrentGameplay(m_Generation, m_LevelData);

            internal void Bind()
            {
                m_Combo.comboDidChangeEvent += OnCombo;
                m_Score.scoreDidChangeEvent += OnScore;
                m_Energy.gameEnergyDidChangeEvent += OnEnergy;
                if (m_Beatmap != null)
                {
                    m_Beatmap.noteWasCutEvent += OnNoteCut;
                    m_Beatmap.noteWasMissedEvent += OnNoteMissed;
                }
                if (m_Pause)
                {
                    m_Pause.didPauseEvent += OnPause;
                    m_Pause.didResumeEvent += OnResume;
                }
            }

            internal void Unbind()
            {
                if (m_Combo) m_Combo.comboDidChangeEvent -= OnCombo;
                if (m_Score) m_Score.scoreDidChangeEvent -= OnScore;
                if (m_Energy) m_Energy.gameEnergyDidChangeEvent -= OnEnergy;
                if (m_Beatmap != null)
                {
                    m_Beatmap.noteWasCutEvent -= OnNoteCut;
                    m_Beatmap.noteWasMissedEvent -= OnNoteMissed;
                }
                if (m_Pause)
                {
                    m_Pause.didPauseEvent -= OnPause;
                    m_Pause.didResumeEvent -= OnResume;
                }
            }

            private void OnCombo(int p_Combo) { if (IsCurrent) ComboController_comboDidChangeEvent(p_Combo); }
            private void OnScore(int p_RawScore, int p_Score) { if (IsCurrent) ScoreController_scoreDidChangeEvent(p_RawScore, p_Score); }
            private void OnEnergy(float p_Health) { if (IsCurrent) GameEnergyCounter_gameEnergyDidChangeEvent(p_Health); }
            private void OnNoteCut(NoteController p_Note, in NoteCutInfo p_Info) { if (IsCurrent) ScoreController_noteWasCutEvent(p_Note, in p_Info); }
            private void OnNoteMissed(NoteController p_Note) { if (IsCurrent) ScoreController_noteWasMissedEvent(p_Note); }

            private void OnPause()
            {
                if (!IsCurrent) return;
                m_IsPaused = true;
                m_PauseEvent.pauseTime = m_AudioTimeSyncController.songTime;
                ++m_PauseRevision;
            }

            private void OnResume()
            {
                if (!IsCurrent) return;
                m_IsPaused = false;
                m_ResumeEvent.resumeTime = m_AudioTimeSyncController.songTime;
                ++m_ResumeRevision;
            }
        }

        private sealed class CoverRequest
        {
            private readonly byte[] m_Pixels;
            private readonly GraphicsFormat m_Format;
            private readonly uint m_Width;
            private readonly uint m_Height;

            internal readonly TaskCompletionSource<string> Completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            internal CoverRequest(byte[] p_Pixels, GraphicsFormat p_Format, uint p_Width, uint p_Height)
            {
                m_Pixels = p_Pixels;
                m_Format = p_Format;
                m_Width = p_Width;
                m_Height = p_Height;
            }

            internal string Encode()
            {
                try
                {
                    return Convert.ToBase64String(ImageConversion.EncodeArrayToPNG(m_Pixels, m_Format, m_Width, m_Height));
                }
                catch
                {
                    return "";
                }
            }
        }

        private static class CoverEncoder
        {
            private static readonly object m_Gate = new object();
            private static CoverRequest m_Current;
            private static CoverRequest m_Pending;
            private static Task<string> m_Running;

            internal static Task<string> Queue(CoverRequest p_Request)
            {
                lock (m_Gate)
                {
                    m_Pending?.Completion.TrySetCanceled();
                    m_Pending = p_Request;
                    if (m_Running == null)
                        StartPending();
                }
                return p_Request.Completion.Task;
            }

            internal static void DiscardPending()
            {
                lock (m_Gate)
                {
                    m_Pending?.Completion.TrySetCanceled();
                    m_Pending = null;
                }
            }

            private static void StartPending()
            {
                m_Current = m_Pending;
                m_Pending = null;
                try
                {
                    m_Running = Task.Run(m_Current.Encode);
                }
                catch
                {
                    m_Current.Completion.TrySetResult("");
                    m_Current = null;
                    return;
                }
                m_Running.ContinueWith(OnCompleted, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }

            private static void OnCompleted(Task<string> p_Task)
            {
                lock (m_Gate)
                {
                    if (!ReferenceEquals(m_Running, p_Task))
                        return;

                    // Retirement only discards pending work; the running slot lasts until physical completion.
                    m_Running = null;
                    m_Current.Completion.TrySetResult(p_Task.Status == TaskStatus.RanToCompletion ? p_Task.Result : "");
                    if (p_Task.IsFaulted)
                        _ = p_Task.Exception;
                    m_Current = null;
                    if (m_Pending != null)
                        StartPending();
                }
            }
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Initialize external overlay server
        /// </summary>
        internal static void Start()
        {
            if (m_CoverEnabled)
                return;
            m_CoverEnabled = true;
            var l_Generation = ++m_ServerGeneration;
            ++m_GameplayGeneration;
            CoverEncoder.DiscardPending();
            m_GameStateEvent.FeedEvent();
            m_MapInfoEvent.FeedEvent();
            m_PauseEvent.FeedEvent();
            m_ResumeEvent.FeedEvent();
            m_ScoreEvent.FeedEvent();
            CP_SDK_BS.Game.Logic.OnSceneChange += Logic_OnSceneChange;
            Application.quitting += Stop;
            CP_SDK.Unity.MTCoroutineStarter.Start(Coroutine_StartServer(l_Generation));
            Logic_OnSceneChange(CP_SDK_BS.Game.Logic.ActiveScene);
        }

        internal static void Stop()
        {
            m_CoverEnabled = false;
            ++m_ServerGeneration;
            ++m_GameplayGeneration;
            CoverEncoder.DiscardPending();
            Application.quitting -= Stop;
            CP_SDK_BS.Game.Logic.OnSceneChange -= Logic_OnSceneChange;
            m_Transport?.Stop();
            m_Transport = null;
            ReleaseGameplay();
        }

        private static bool IsCurrentServer(long p_Generation)
        {
            return m_CoverEnabled && m_ServerGeneration == p_Generation;
        }

        private static IEnumerator Coroutine_StartServer(long p_Generation)
        {
            if (!IsCurrentServer(p_Generation))
                yield break;
            var l_WaitCount = 0;
            var l_UserID = CP_SDK_BS.Game.UserPlatform.GetUserID();
            var l_PlatformDelay = new WaitForSecondsRealtime(1f);
            while (string.IsNullOrEmpty(l_UserID) && l_WaitCount < 20)
            {
                yield return l_PlatformDelay;
                if (!IsCurrentServer(p_Generation))
                    yield break;
                l_UserID = CP_SDK_BS.Game.UserPlatform.GetUserID();
                ++l_WaitCount;
            }

            // A replacement cannot bind until the previous worker has finished server shutdown.
            while (m_TransportTask != null && !m_TransportTask.IsCompleted)
            {
                if (!IsCurrentServer(p_Generation))
                    yield break;
                yield return null;
            }
            if (!IsCurrentServer(p_Generation))
                yield break;
            if (m_TransportTask != null && m_TransportTask.IsFaulted)
                _ = m_TransportTask.Exception;

            OverlayTransport l_Transport = null;
            Task l_Task = null;
            try
            {
#if DEBUG
                const bool l_Indented = true;
#else
                const bool l_Indented = false;
#endif
                var l_Startup = new OverlayTransport.Startup(Application.version, CP_SDK_BS.Game.UserPlatform.GetUserName(), l_UserID, l_Indented);
                if (IsCurrentServer(p_Generation))
                {
                    l_Transport = new OverlayTransport(l_Startup);
                    m_Transport = l_Transport;
                    l_Task = Task.Run(l_Transport.Run);
                    m_TransportTask = l_Task;
                    l_Task.ContinueWith(OverlayTransport.ObserveFault, System.Threading.CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
            catch (Exception l_Exception)
            {
                l_Transport?.Stop();
                if (ReferenceEquals(m_Transport, l_Transport))
                    m_Transport = null;
                Logger.Instance.Error(l_Exception);
            }
            if (l_Task == null)
                yield break;
            m_TransportTask = l_Task;

            var l_PumpDelay = new WaitForSecondsRealtime(0.033f);
            while (IsCurrentServer(p_Generation) && ReferenceEquals(m_Transport, l_Transport) && !l_Task.IsCompleted)
            {
                try { Pump(p_Generation, l_Transport); }
                catch (Exception l_Exception) { Logger.Instance.Error(l_Exception); }
                yield return l_PumpDelay;
            }
            if (ReferenceEquals(m_Transport, l_Transport))
                m_Transport = null;
        }

        private static void Pump(long p_Generation, OverlayTransport p_Transport)
        {
            if (p_Transport.NeedsInitialSnapshot)
            {
                var l_Initial = CaptureInitial();
                if (!IsCurrentServer(p_Generation) || !ReferenceEquals(m_Transport, p_Transport))
                    return;
                p_Transport.InitializePending(l_Initial);
            }
            if (!p_Transport.HasClients)
                return;
            if (m_MapInfoRevision == m_MapInfoPublishedRevision && m_GameStateRevision == m_GameStatePublishedRevision
                && m_ResumeRevision == m_ResumePublishedRevision && m_PauseRevision == m_PausePublishedRevision
                && m_ScoreRevision == m_ScorePublishedRevision)
                return;

            var l_Revisions = new[] { m_MapInfoRevision, m_GameStateRevision, m_ResumeRevision, m_PauseRevision, m_ScoreRevision };
            var l_Published = new[] { m_MapInfoPublishedRevision, m_GameStatePublishedRevision, m_ResumePublishedRevision, m_PausePublishedRevision, m_ScorePublishedRevision };
            var l_Events = new[] { m_MapInfoEvent, m_GameStateEvent, m_ResumeEvent, m_PauseEvent, m_ScoreEvent };
            var l_Updates = new OverlayTransport.Payload[5];
            var l_HasUpdates = false;
            for (int l_I = 0; l_I < l_Updates.Length; ++l_I)
            {
                if (l_Revisions[l_I] == l_Published[l_I])
                    continue;
                l_Updates[l_I] = new OverlayTransport.Payload(CloneEvent(l_Events[l_I]));
                l_HasUpdates = true;
            }
            if (!l_HasUpdates || !IsCurrentServer(p_Generation) || !ReferenceEquals(m_Transport, p_Transport) || !p_Transport.PublishUpdates(l_Updates))
                return;

            // Reentrant custom serialization can create later revisions; only acknowledge the captured ones.
            if (l_Updates[0] != null) m_MapInfoPublishedRevision = l_Revisions[0];
            if (l_Updates[1] != null) m_GameStatePublishedRevision = l_Revisions[1];
            if (l_Updates[2] != null) m_ResumePublishedRevision = l_Revisions[2];
            if (l_Updates[3] != null) m_PausePublishedRevision = l_Revisions[3];
            if (l_Updates[4] != null) m_ScorePublishedRevision = l_Revisions[4];
        }

        private static OverlayTransport.Payload[] CaptureInitial()
        {
            var l_Events = new List<Models.Event>();
            if (CP_SDK_BS.Game.Logic.ActiveScene == CP_SDK_BS.Game.Logic.ESceneType.Playing)
            {
                l_Events.Add(CloneEvent(m_MapInfoEvent));
                l_Events.Add(CloneEvent(m_ScoreEvent));
                l_Events.Add(CloneEvent(m_GameStateEvent));
                if (m_PauseController && m_AudioTimeSyncController)
                {
                    if (m_IsPaused)
                    {
                        m_PauseEvent.pauseTime = m_AudioTimeSyncController.songTime;
                        l_Events.Add(CloneEvent(m_PauseEvent));
                    }
                    else
                    {
                        m_ResumeEvent.resumeTime = m_AudioTimeSyncController.songTime;
                        l_Events.Add(CloneEvent(m_ResumeEvent));
                    }
                }
            }
            else
                l_Events.Add(CloneEvent(m_GameStateEvent));
            var l_Result = new OverlayTransport.Payload[l_Events.Count];
            for (int l_I = 0; l_I < l_Result.Length; ++l_I)
                l_Result[l_I] = new OverlayTransport.Payload(l_Events[l_I]);
            return l_Result;
        }

        private static Models.Event CloneEvent(Models.Event p_Event)
        {
            var l_Result = new Models.Event()
            {
                gameStateChanged = p_Event.gameStateChanged,
                pauseTime = p_Event.pauseTime,
                resumeTime = p_Event.resumeTime,
            };
            if (p_Event.mapInfoChanged != null)
            {
                var l_Map = p_Event.mapInfoChanged;
                l_Result.mapInfoChanged = new Models.MapInfo()
                {
                    level_id = l_Map.level_id, name = l_Map.name, sub_name = l_Map.sub_name,
                    artist = l_Map.artist, mapper = l_Map.mapper, characteristic = l_Map.characteristic,
                    difficulty = l_Map.difficulty, BSRKey = l_Map.BSRKey, coverRaw = l_Map.coverRaw,
                    duration = l_Map.duration, BPM = l_Map.BPM, PP = l_Map.PP,
                    time = l_Map.time, timeMultiplier = l_Map.timeMultiplier,
                };
            }
            if (p_Event.scoreEvent != null)
            {
                var l_Score = p_Event.scoreEvent;
                l_Result.scoreEvent = new Models.Score()
                {
                    time = l_Score.time, accuracy = l_Score.accuracy, currentHealth = l_Score.currentHealth,
                    score = l_Score.score, combo = l_Score.combo, missCount = l_Score.missCount,
                };
            }
            l_Result.FeedEvent();
            return l_Result;
        }

        private static void ReleaseGameplay()
        {
            var l_Previous = m_GameplaySubscriptions;
            m_GameplaySubscriptions = null;
            l_Previous?.Unbind();
            m_AudioTimeSyncController = null;
            m_ScoreController = null;
            m_ComboController = null;
            m_GameEnergyCounter = null;
            m_PauseController = null;
        }

        private static void Logic_OnSceneChange(CP_SDK_BS.Game.Logic.ESceneType p_Scene)
        {
            if (!m_CoverEnabled)
                return;

            var l_Generation = ++m_GameplayGeneration;
            CoverEncoder.DiscardPending();
            ReleaseGameplay();

            if (p_Scene == CP_SDK_BS.Game.Logic.ESceneType.Playing)
            {
                m_IsPaused = false;

                var l_Map = CP_SDK_BS.Game.Logic.LevelData;
                if (l_Map == null)
                    return;

                var l_WorkingMapInfo = m_MapInfoEvent.mapInfoChanged;
                var l_CoverTask      = null as Task<Sprite>;

                var l_MapChanged = l_WorkingMapInfo.level_id != l_Map.Data.beatmapLevel.levelID;
                if (l_MapChanged || !m_CoverReady)
                {
                    try { l_CoverTask = l_Map.Data.beatmapLevel.previewMediaData?.GetCoverSpriteAsync(); } catch { }
                    m_CoverReady = l_CoverTask == null;
                    if (l_CoverTask != null)
                        l_CoverTask.ContinueWith(ObserveCoverFault, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }

                if (l_MapChanged)
                {
                    l_WorkingMapInfo.level_id   = l_Map.Data.beatmapLevel.levelID;
                    l_WorkingMapInfo.name       = l_Map.Data.beatmapLevel.songName;
                    l_WorkingMapInfo.sub_name   = l_Map.Data.beatmapLevel.songSubName;
                    l_WorkingMapInfo.artist     = l_Map.Data.beatmapLevel.songAuthorName;
                    l_WorkingMapInfo.mapper     = l_Map.Data.beatmapLevel.allMappers?.FirstOrDefault() ?? "";
                    l_WorkingMapInfo.duration   = (uint)(l_Map.Data.beatmapLevel.songDuration * 1000f);
                    l_WorkingMapInfo.BPM        = l_Map.Data.beatmapLevel.beatsPerMinute;
                    l_WorkingMapInfo.PP         = 0f;
                    l_WorkingMapInfo.BSRKey     = "";
                }

                l_WorkingMapInfo.characteristic = l_Map.Data.beatmapKey.characteristic.ToString();
                l_WorkingMapInfo.difficulty     = l_Map.Data.beatmapKey.difficulty.ToString();

                CP_SDK.Unity.MTCoroutineStarter.Start(Coroutine_WaitForGameplayReady(l_Map.Type, l_CoverTask, l_Generation, l_Map));
            }
            else if (p_Scene == CP_SDK_BS.Game.Logic.ESceneType.Menu)
            {
                m_IsPaused                  = false;
                m_ScoreController           = null;
                m_AudioTimeSyncController   = null;
                m_PauseController           = null;

                m_GameStateEvent.gameStateChanged = p_Scene.ToString();

                ++m_GameStateRevision;
            }
        }
        private static void ObserveCoverFault(Task<Sprite> p_Task)
        {
            _ = p_Task.Exception;
        }

        private static bool IsCurrentGameplay(long p_Generation, object p_LevelData)
        {
            return m_CoverEnabled && m_GameplayGeneration == p_Generation
                && CP_SDK_BS.Game.Logic.ActiveScene == CP_SDK_BS.Game.Logic.ESceneType.Playing
                && ReferenceEquals(CP_SDK_BS.Game.Logic.LevelData, p_LevelData);
        }

        /// <summary>
        /// On gameplay start coroutine
        /// </summary>
        /// <returns></returns>
        private static IEnumerator Coroutine_WaitForGameplayReady(CP_SDK_BS.Game.LevelType p_Type, Task<Sprite> p_CoverTask, long p_Generation, object p_LevelData)
        {
            if (!IsCurrentGameplay(p_Generation, p_LevelData))
                yield break;

            if (p_CoverTask != null)
            {
                while (!p_CoverTask.IsCompleted)
                {
                    if (!IsCurrentGameplay(p_Generation, p_LevelData))
                        yield break;
                    yield return null;
                }
                if (!IsCurrentGameplay(p_Generation, p_LevelData))
                    yield break;

                var l_BackupRenderTexture = RenderTexture.active;
                RenderTexture l_NewRenderTexture = null;
                Texture2D l_NewCover = null;
                CoverRequest l_Request = null;
                try
                {
                    var l_Texture           = p_CoverTask.Result.texture;
                    l_NewRenderTexture = RenderTexture.GetTemporary(l_Texture.width, l_Texture.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);

                    Graphics.Blit(l_Texture, l_NewRenderTexture);
                    RenderTexture.active = l_NewRenderTexture;

                    var l_Rect  = p_CoverTask.Result.rect;
                    var l_UV    = p_CoverTask.Result.uv[0];

                    l_NewCover = new Texture2D((int)l_Rect.width, (int)l_Rect.height);

                    l_NewCover.ReadPixels(new Rect(
                        l_UV.x * l_Texture.width,
                        l_Texture.height - l_UV.y * l_Texture.height,
                        l_Rect.width,
                        l_Rect.height
                    ), 0, 0);
                    l_NewCover.Apply();

                    l_Request = new CoverRequest(l_NewCover.GetRawTextureData(), l_NewCover.graphicsFormat, (uint)l_NewCover.width, (uint)l_NewCover.height);
                }
                catch
                {
                    l_Request = null;
                }
                finally
                {
                    RenderTexture.active = l_BackupRenderTexture;
                    if (l_NewRenderTexture != null)
                        RenderTexture.ReleaseTemporary(l_NewRenderTexture);
                    if (l_NewCover != null)
                        UnityEngine.Object.Destroy(l_NewCover);
                }

                if (!IsCurrentGameplay(p_Generation, p_LevelData))
                    yield break;

                if (l_Request != null)
                {
                    var l_Encoding = CoverEncoder.Queue(l_Request);
                    while (!l_Encoding.IsCompleted)
                    {
                        if (!IsCurrentGameplay(p_Generation, p_LevelData))
                            yield break;
                        yield return null;
                    }
                    if (!IsCurrentGameplay(p_Generation, p_LevelData))
                        yield break;
                    m_MapInfoEvent.mapInfoChanged.coverRaw = l_Encoding.Status == TaskStatus.RanToCompletion ? l_Encoding.Result : "";
                }
                else
                    m_MapInfoEvent.mapInfoChanged.coverRaw = "";
                m_CoverReady = true;
            }

            yield return new WaitUntil(() => !IsCurrentGameplay(p_Generation, p_LevelData) || Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().LastOrDefault());
            if (!IsCurrentGameplay(p_Generation, p_LevelData))
                yield break;
            yield return new WaitUntil(() => !IsCurrentGameplay(p_Generation, p_LevelData) || Resources.FindObjectsOfTypeAll<ScoreController>().LastOrDefault());
            if (!IsCurrentGameplay(p_Generation, p_LevelData))
                yield break;
            yield return new WaitUntil(() => !IsCurrentGameplay(p_Generation, p_LevelData) || Resources.FindObjectsOfTypeAll<ComboController>().LastOrDefault());
            if (!IsCurrentGameplay(p_Generation, p_LevelData))
                yield break;
            yield return new WaitUntil(() => !IsCurrentGameplay(p_Generation, p_LevelData) || Resources.FindObjectsOfTypeAll<GameEnergyCounter>().LastOrDefault());
            if (!IsCurrentGameplay(p_Generation, p_LevelData))
                yield break;

            if (p_Type != CP_SDK_BS.Game.LevelType.Multiplayer)
            {
                yield return new WaitUntil(() => !IsCurrentGameplay(p_Generation, p_LevelData) || Resources.FindObjectsOfTypeAll<PauseController>().LastOrDefault());
                if (!IsCurrentGameplay(p_Generation, p_LevelData))
                    yield break;
            }

            m_AudioTimeSyncController   = Resources.FindObjectsOfTypeAll<AudioTimeSyncController>().LastOrDefault();
            m_ScoreController           = Resources.FindObjectsOfTypeAll<ScoreController>().LastOrDefault();
            m_ComboController           = Resources.FindObjectsOfTypeAll<ComboController>().LastOrDefault();
            m_GameEnergyCounter         = Resources.FindObjectsOfTypeAll<GameEnergyCounter>().LastOrDefault();

            m_MapInfoEvent.mapInfoChanged.time              = m_AudioTimeSyncController.songTime;
            m_MapInfoEvent.mapInfoChanged.timeMultiplier    = m_AudioTimeSyncController.timeScale;

            ++m_MapInfoRevision;

            m_GameStateEvent.gameStateChanged = CP_SDK_BS.Game.Logic.ActiveScene.ToString();

            ++m_GameStateRevision;

            m_ScoreEvent.scoreEvent.time            = m_AudioTimeSyncController.songTime;
            m_ScoreEvent.scoreEvent.score           = 0;
            m_ScoreEvent.scoreEvent.accuracy        = 1f;
            m_ScoreEvent.scoreEvent.combo           = 0;
            m_ScoreEvent.scoreEvent.missCount       = 0;
            m_ScoreEvent.scoreEvent.currentHealth   = m_GameEnergyCounter.energy;

            ++m_ScoreRevision;

            if (p_Type != CP_SDK_BS.Game.LevelType.Multiplayer)
                m_PauseController = Resources.FindObjectsOfTypeAll<PauseController>().LastOrDefault();

            m_GameplaySubscriptions = new GameplaySubscriptions(p_Generation, p_LevelData);
            m_GameplaySubscriptions.Bind();

            if (m_PauseController)
            {
                m_IsPaused = m_PauseController._paused == PauseController.PauseState.Paused;
            }
            else
                m_IsPaused = false;

            if (m_IsPaused)
            {
                m_PauseEvent.pauseTime  = m_AudioTimeSyncController.songTime;
                ++m_PauseRevision;
            }
            else
            {
                m_ResumeEvent.resumeTime    = m_AudioTimeSyncController.songTime;
                ++m_ResumeRevision;
            }
        }
        /// <summary>
        /// Note was cut
        /// </summary>
        /// <param name="p_NoteController">Note controller</param>
        /// <param name="p_NoteCutInfo">Note Cut info</param>
        private static void ScoreController_noteWasCutEvent(NoteController p_NoteController, in NoteCutInfo p_NoteCutInfo)
        {
            if (   p_NoteCutInfo.noteData.scoringType   == NoteData.ScoringType.Ignore
                || p_NoteCutInfo.failReason             == NoteCutInfo.FailReason.None
                || p_NoteCutInfo.noteData.colorType     == ColorType.None)
                return;

            m_ScoreEvent.scoreEvent.missCount++;
            ++m_ScoreRevision;
        }
        /// <summary>
        /// Note was missed
        /// </summary>
        /// <param name="p_Note">Missed note data</param>
        private static void ScoreController_noteWasMissedEvent(NoteController p_Note)
        {
            if (   p_Note.noteData.scoringType  == NoteData.ScoringType.Ignore
                || p_Note.noteData.colorType    == ColorType.None)
                return;

            m_ScoreEvent.scoreEvent.missCount++;
            ++m_ScoreRevision;
        }
        /// <summary>
        /// Combo did change
        /// </summary>
        /// <param name="p_Combo">New combo</param>
        private static void ComboController_comboDidChangeEvent(int p_Combo)
        {
            m_ScoreEvent.scoreEvent.time            = m_AudioTimeSyncController.songTime;
            m_ScoreEvent.scoreEvent.combo           = (uint)p_Combo;
            ++m_ScoreRevision;
        }
        /// <summary>
        /// On score change
        /// </summary>
        /// <param name="p_RawScore">Raw score</param>
        /// <param name="p_Score">Modified score</param>
        private static void ScoreController_scoreDidChangeEvent(int p_RawScore, int p_Score)
        {
            m_ScoreEvent.scoreEvent.time            = m_AudioTimeSyncController.songTime;
            m_ScoreEvent.scoreEvent.score           = (uint)p_RawScore;
            m_ScoreEvent.scoreEvent.accuracy        = (float)p_Score / (float)m_ScoreController.immediateMaxPossibleMultipliedScore;
            ++m_ScoreRevision;
        }
        /// <summary>
        /// On game energy change
        /// </summary>
        /// <param name="p_Health">New health</param>
        private static void GameEnergyCounter_gameEnergyDidChangeEvent(float p_Health)
        {
            m_ScoreEvent.scoreEvent.time            = m_AudioTimeSyncController.songTime;
            m_ScoreEvent.scoreEvent.currentHealth   = p_Health;
            ++m_ScoreRevision;
        }

        ////////////////////////////////////////////////////////////////////////////
        ////////////////////////////////////////////////////////////////////////////

    }
}
